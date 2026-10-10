using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;

using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.Extensions.Options;

using VipsImage = NetVips.Image;

namespace backend.tests.Unit.Shared.Storage.Imaging;

/// <remarks>
/// Every fixture is generated here with libvips or assembled byte by byte, so no binary assets
/// live in the repository.
/// </remarks>
public class NetVipsImageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldRemoveGpsExif_AndOutputWebp()
    {
        using var input = TestImages.Solid(64, 48, 120, 160, 200);
        var jpeg = TestImages.WithExif(TestImages.Jpeg(input), gps: true);
        TestImages.MetadataFields(jpeg).Should().Contain("exif-data", "the fixture has to carry GPS to prove anything");

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        ImageSignatureInspector.TryDetect(result.Content, out var signature).Should().BeTrue();
        signature.Format.Should().Be(ImageFormat.Webp);
        result.ContentType.Should().Be("image/webp");
        result.FileExtension.Should().Be(".webp");

        TestImages.MetadataFields(result.Content).Should().BeEmpty();
        TestImages.WebpChunks(result.Content).Should().NotContain(["EXIF", "XMP ", "ICCP"]);
    }

    [Fact]
    public async Task ProcessAsync_ShouldCapAvatarAtLongEdge512()
    {
        using var input = TestImages.Solid(4000, 3000, 10, 20, 30);
        var jpeg = TestImages.Jpeg(input);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(512);
        Decoded(result).Height.Should().Be(384);
        result.Width.Should().Be(512);
        result.Height.Should().Be(384);
    }

    [Fact]
    public async Task ProcessAsync_ShouldCapGalleryImagesAtTheirOwnLongEdge()
    {
        using var input = TestImages.Solid(4000, 3000, 10, 20, 30);
        var jpeg = TestImages.Jpeg(input);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Gallery);

        Decoded(result).Width.Should().Be(2048);
        Decoded(result).Height.Should().Be(1536);
    }

    [Fact]
    public async Task ProcessAsync_ShouldHonourAConfiguredGalleryEdge()
    {
        using var input = TestImages.Solid(1000, 500, 10, 20, 30);

        var result = await CreateProcessor(new ImageProcessingOptions { GalleryMaxEdge = 300 })
            .ProcessAsync(new MemoryStream(TestImages.Png(input)), ImageProcessingProfile.Gallery);

        Decoded(result).Width.Should().Be(300);
        Decoded(result).Height.Should().Be(150);
    }

    [Fact]
    public async Task ProcessAsync_ShouldReportTheDimensionsOfTheEncodedOutput_AfterOrienting()
    {
        // Stored landscape and tagged "rotate 90": the reported size is the upright one that was
        // encoded, which is what a MediaAsset records.
        using var input = TestImages.Solid(80, 40, 1, 2, 3);
        var jpeg = TestImages.WithExif(TestImages.Jpeg(input), orientation: 6);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Gallery);

        result.Width.Should().Be(40);
        result.Height.Should().Be(80);
        Decoded(result).Width.Should().Be(result.Width);
        Decoded(result).Height.Should().Be(result.Height);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAnUnknownProfile()
    {
        using var input = TestImages.Solid(8, 8, 0, 0, 0);

        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(TestImages.Png(input)), (ImageProcessingProfile)99);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotUpscaleSmallImages()
    {
        using var input = TestImages.Solid(100, 80, 10, 20, 30);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(TestImages.Png(input)), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(100);
        Decoded(result).Height.Should().Be(80);
    }

    [Fact]
    public async Task ProcessAsync_ShouldApplyExifOrientationBeforeStrippingIt()
    {
        // Stored landscape, red on the left and blue on the right, tagged orientation 6: "rotate
        // 90 degrees clockwise to display", which is how phones save portrait shots. Displayed
        // upright it is portrait with red on top. Stripping EXIF without applying the tag first
        // would store it landscape.
        using var input = TestImages.RedLeftBlueRight(80, 40);
        var jpeg = TestImages.WithExif(TestImages.Jpeg(input), orientation: 6);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(40);
        Decoded(result).Height.Should().Be(80);

        TestImages.MetadataFields(result.Content).Should().BeEmpty();
        using var stored = VipsImage.NewFromBuffer(result.Content);
        IsMostly(stored.Getpoint(20, 15), red: true).Should().BeTrue("the top half should be the red side");
        IsMostly(stored.Getpoint(20, 65), red: false).Should().BeTrue("the bottom half should be the blue side");
    }

    [Fact]
    public async Task ProcessAsync_ShouldStillOrientCorrectly_WhenTheImageIsShrunkFirst()
    {
        // The image is shrunk before it is oriented, so a full-resolution rotation never
        // allocates a second full-size buffer. The orientation has to survive the resize for
        // this to work.
        using var input = TestImages.RedLeftBlueRight(2000, 1000);
        var jpeg = TestImages.WithExif(TestImages.Jpeg(input), orientation: 6);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(256);
        Decoded(result).Height.Should().Be(512);

        using var stored = VipsImage.NewFromBuffer(result.Content);
        IsMostly(stored.Getpoint(128, 64), red: true).Should().BeTrue("the top half should be the red side");
        IsMostly(stored.Getpoint(128, 448), red: false).Should().BeTrue("the bottom half should be the blue side");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectADecompressionBombFromItsHeaderAlone()
    {
        // 100000 x 100000 RGBA is 40 GB of pixels declared by a file of about 70 bytes. Were the
        // header probe skipped, the decode would try to allocate it and fail on memory, with a
        // different message, and allocate far more than this test allows.
        var processor = CreateProcessor();
        var bomb = CraftPng(100_000, 100_000);

        // Warm up so JIT and libvips' one-time setup are not counted against the probe.
        await processor.Invoking(p => p.ProcessAsync(new MemoryStream(bomb), ImageProcessingProfile.Avatar))
            .Should().ThrowAsync<BadRequestException>();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var act = () => processor.ProcessAsync(new MemoryStream(bomb), ImageProcessingProfile.Avatar);
        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.TooLargeMessage);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(4 * 1024 * 1024);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectTooManyPixels_EvenWhenEachSideIsWithinLimits()
    {
        // 7000 x 7500: both sides under 8000, but 52.5 MP is over the 50 MP cap.
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(CraftPng(7000, 7500)), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.TooLargeMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAnimatedGif()
    {
        using var first = TestImages.Solid(16, 16, 255, 0, 0);
        using var second = TestImages.Solid(16, 16, 0, 0, 255);
        using var animation = TestImages.Animation(first, second);
        var gif = TestImages.Gif(animation);

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(gif), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldConvertStaticGifToWebp()
    {
        using var input = TestImages.Solid(16, 16, 255, 0, 0);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(TestImages.Gif(input)), ImageProcessingProfile.Avatar);

        ImageSignatureInspector.TryDetect(result.Content, out var signature).Should().BeTrue();
        signature.Format.Should().Be(ImageFormat.Webp);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptAGreyscalePng()
    {
        // The encoder is handed 8-bit sRGB whatever came in, so a single-band image converts
        // rather than failing as a server fault.
        using var black = VipsImage.Black(20, 10);
        using var grey = (black + 128).Cast(NetVips.Enums.BandFormat.Uchar);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(grey.PngsaveBuffer()), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(20);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptASixteenBitPng()
    {
        using var input = TestImages.Solid(20, 10, 200, 100, 50);
        using var wide = (input * 257).Cast(NetVips.Enums.BandFormat.Ushort);
        using var rgb16 = wide.Copy(interpretation: NetVips.Enums.Interpretation.Rgb16);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(rgb16.PngsaveBuffer(bitdepth: 16)), ImageProcessingProfile.Avatar);

        using var stored = VipsImage.NewFromBuffer(result.Content);
        stored.Width.Should().Be(20);
        stored.Format.Should().Be(NetVips.Enums.BandFormat.Uchar);
        stored.Getpoint(5, 5)[0].Should().BeApproximately(200, 4, "16-bit values scale down to 8 bits, not clip");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectBytesThatAreNotAnImage()
    {
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream([0x01, 0x02, 0x03, 0x04]), ImageProcessingProfile.Avatar);

        // 400, not 415: [ImageContent] model validation already answers 400 for the same file,
        // and the published contract documents that.
        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnsupportedFormatMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectFormatsOutsideTheAllowlist()
    {
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(TestImages.Bmp()), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnsupportedFormatMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAHeaderThatCannotBeDecoded()
    {
        // A PNG signature followed by nothing a decoder can use.
        byte[] corrupt = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48];

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(corrupt), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnreadableMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldQueueRequestsBeyondTheConcurrencyLimit()
    {
        var processor = CreateProcessor(new ImageProcessingOptions { MaxConcurrentOperations = 1 });
        using var input = TestImages.Solid(32, 32, 1, 2, 3);
        var png = TestImages.Png(input);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar))));

        results.Should().AllSatisfy(result => Decoded(result).Width.Should().Be(32));
    }

    [Fact]
    public async Task ProcessAsync_WhenCancelled_ShouldThrowAndLeaveTheSlotFree()
    {
        var processor = CreateProcessor(new ImageProcessingOptions { MaxConcurrentOperations = 1 });
        using var input = TestImages.Solid(32, 32, 1, 2, 3);
        var png = TestImages.Png(input);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var cancelled = () => processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar, cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        // With a single slot, a leaked one would make this wait forever.
        var next = processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar);
        (await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(next);
        Decoded(await next).Width.Should().Be(32);
    }

    [Fact]
    public async Task ProcessAsync_WhenCancelledMidDecode_ShouldStopAndSurfaceCancellation()
    {
        // The token reaches libvips' evaluation, not just the gaps between stages: a request that
        // goes away part-way through a large decode stops holding its slot.
        var processor = CreateProcessor(new ImageProcessingOptions { MaxConcurrentOperations = 1 });
        using var input = TestImages.Pattern(
            7000, 7000,
            (x, _) => x % 251,
            (_, y) => y % 241,
            (x, y) => (x + y) % 239);
        var png = TestImages.Png(input);

        var uncancelled = System.Diagnostics.Stopwatch.StartNew();
        await processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar);
        uncancelled.Stop();

        using var cts = new CancellationTokenSource(uncancelled.Elapsed / 4);
        var cancelled = System.Diagnostics.Stopwatch.StartNew();
        var act = () => processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        cancelled.Stop();

        // Measured at about 0.6 of the uncancelled time with the token wired in, and the full time
        // without it: cancellation lands between libvips' work units, not instantly.
        cancelled.Elapsed.Should().BeLessThan(uncancelled.Elapsed * 0.8, "cancellation has to stop the decode, not wait for it to finish");
    }

    [Fact]
    public async Task ProcessAsync_ShouldStripEveryMetadataProfile_EvenWhenTheInputCarriesThemAll()
    {
        // Keeping no metadata at encode time is the only mechanism that drops it, so this asserts
        // the encoded output directly rather than trusting an in-memory clean-up step.
        using var input = TestImages.Solid(40, 30, 90, 140, 190);
        var jpeg = TestImages.WithIptcByline(
            TestImages.WithExif(TestImages.Jpeg(input), software: "definitely-not-wanted"),
            "someone");
        TestImages.MetadataFields(jpeg).Should().Contain(["exif-data", "iptc-data"], "the fixture has to carry both to prove anything");

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        TestImages.MetadataFields(result.Content).Should().BeEmpty();
        TestImages.WebpChunks(result.Content).Should().NotContain(["EXIF", "XMP ", "ICCP"]);
        result.Content.AsSpan().IndexOf("definitely-not-wanted"u8).Should().Be(-1, "the EXIF string must not survive anywhere in the encoded bytes");
        result.Content.AsSpan().IndexOf("someone"u8).Should().Be(-1, "the IPTC by-line must not survive anywhere in the encoded bytes");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAnimatedWebp()
    {
        using var first = TestImages.Solid(32, 32, 255, 0, 0);
        using var second = TestImages.Solid(32, 32, 0, 0, 255);
        using var animation = TestImages.Animation(first, second);
        var webp = TestImages.Webp(animation);

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(webp), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectApngAsAnimated_NotFlattenIt()
    {
        // libvips reads only an APNG's default image, so left to the decoder the animation would
        // be silently flattened to its first frame. The acTL chunk is checked before decoding so
        // it is refused with the same message as GIF and WebP.
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(CraftApng()), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptAStillPngWhateverItsPixelBytesContain()
    {
        // Guards the acTL scan against matching bytes inside pixel data: the scan walks chunk
        // headers and stops at IDAT rather than searching the whole file.
        using var input = TestImages.Pattern(
            64, 64,
            (x, _) => (x * 7) % 256,
            (_, y) => (y * 5) % 256,
            (x, y) => x ^ y);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(TestImages.Png(input)), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(64);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAPngWhoseChunkLengthWouldOverflowTheScan()
    {
        // A chunk declaring a length near int.MaxValue: walking it with 32-bit arithmetic wraps
        // the offset negative and makes the next slice throw, which would escape the decoder's
        // exception filter as a 500 from a 30-byte file.
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, int.MaxValue - 4);
        "IHDR"u8.CopyTo(header[4..]);
        stream.Write(header);
        stream.Write([0x00, 0x00, 0x00, 0x00]);

        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(stream.ToArray()), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnreadableMessage);
    }

    [Theory]
    [InlineData((byte)8, (byte)5)]   // colour type 5 does not exist
    [InlineData((byte)3, (byte)6)]   // bit depth 3 is not valid for RGBA
    public async Task ProcessAsync_ShouldReturnBadRequest_ForUnsupportedPngVariants(byte bitDepth, byte colorType)
    {
        // An invalid header is the uploader's file, so it has to come back as a 400 rather than
        // escape the filter as a 500.
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(CraftPng(8, 8, bitDepth, colorType)), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnreadableMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptAStreamThatCannotSeek()
    {
        // The upload is buffered once up front, so a forward-only stream is fine.
        using var input = TestImages.Solid(24, 24, 7, 8, 9);
        var png = TestImages.Png(input);

        var result = await CreateProcessor().ProcessAsync(
            new ForwardOnlyStream(png), ImageProcessingProfile.Avatar);

        Decoded(result).Width.Should().Be(24);
    }

    [Fact]
    public async Task ProcessAsync_ShouldShedLoad_WhenNoSlotComesFreeInTime()
    {
        // Nothing else bounds the queue: the rate limit is per account, so without this an upload
        // waits, holding its buffered body, until the client or the request timeout gives up.
        var processor = CreateProcessor(new ImageProcessingOptions
        {
            MaxConcurrentOperations = 1,
            SlotWaitTimeoutSeconds = 1
        });

        using var input = TestImages.Solid(3000, 3000, 4, 5, 6);
        var png = TestImages.Png(input);
        using var blocker = new SlowStream(png, TimeSpan.FromSeconds(5));

        var occupying = processor.ProcessAsync(blocker, ImageProcessingProfile.Avatar);
        var shed = async () =>
        {
            // Give the first call time to take the only slot.
            await Task.Delay(200);
            await processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar);
        };

        await shed.Should().ThrowAsync<NotAvailableException>()
            .WithMessage(NetVipsImageProcessor.BusyMessage);

        await occupying;
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnBadRequest_ForATruncatedGif()
    {
        // The header survives and the frame data runs out: whatever the decoder makes of it, it
        // is the uploader's 400.
        using var input = TestImages.Pattern(
            64, 64,
            (x, _) => x * 4,
            (_, y) => y * 4,
            (x, y) => (x + y) * 2);
        var gif = TestImages.Gif(input);
        var truncated = gif[..(int)(gif.Length * 0.6)];

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(truncated), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnreadableMessage);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(0.9)]
    public async Task ProcessAsync_ShouldReturnBadRequest_ForAPngTruncatedMidDecode(double keep)
    {
        // Failing inside the decoder rather than before it: the header is intact and the pixel
        // data runs out part-way. libvips decodes lazily, so this only fails once pixels are
        // demanded, and that still has to be the uploader's 400 rather than a fault in encoding.
        using var input = TestImages.Pattern(
            320, 240,
            (x, _) => x % 251,
            (_, y) => y % 241,
            (x, y) => (x * y) % 239);

        var png = TestImages.Png(input);
        var truncated = png[..(int)(png.Length * keep)];

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(truncated), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnreadableMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldReturnBadRequest_ForAJpegTruncatedMidDecode()
    {
        using var input = TestImages.Pattern(
            320, 240,
            (x, _) => x % 251,
            (_, y) => y % 241,
            (x, y) => (x * y) % 239);

        var jpeg = TestImages.Jpeg(input);
        var truncated = jpeg[..(int)(jpeg.Length * 0.5)];

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(truncated), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(NetVipsImageProcessor.UnreadableMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotReportAServerFaultAsABadImage()
    {
        // A broken stream is our problem, not the uploader's. Reporting it as "could not be read"
        // would tell the user their good photo is broken and keep a real fault out of the 500-level
        // alerting.
        using var input = TestImages.Solid(64, 64, 1, 2, 3);
        using var failing = new FailingStream(TestImages.Png(input));

        var act = () => CreateProcessor().ProcessAsync(failing, ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ProcessAsync_ShouldSurfaceCancellation_RatherThanCallingItAnUnreadableImage()
    {
        using var input = TestImages.Solid(64, 64, 1, 2, 3);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(TestImages.Png(input)), ImageProcessingProfile.Avatar, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Options_ShouldFailValidation_WhenOutOfRange()
    {
        var options = new ImageProcessingOptions { MaxDimension = 0, WebpQuality = 101, MaxConcurrentOperations = 0 };
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true)
            .Should().BeFalse();
        results.SelectMany(result => result.MemberNames).Should().BeEquivalentTo(
            nameof(ImageProcessingOptions.MaxDimension),
            nameof(ImageProcessingOptions.WebpQuality),
            nameof(ImageProcessingOptions.MaxConcurrentOperations));
    }

    [Fact]
    public void Options_ShouldPassValidation_WithDefaults()
    {
        var options = new ImageProcessingOptions();

        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true)
            .Should().BeTrue();
    }

    /// <summary>The stored image's size, as a reader of the blob would see it.</summary>
    private static (int Width, int Height) Decoded(ProcessedImage result)
    {
        using var image = VipsImage.NewFromBuffer(result.Content);
        return (image.Width, image.Height);
    }

    private static NetVipsImageProcessor CreateProcessor(ImageProcessingOptions? options = null) =>
        new(Options.Create(options ?? new ImageProcessingOptions()));

    private static bool IsMostly(double[] pixel, bool red) =>
        red
            ? pixel[0] > 180 && pixel[2] < 80
            : pixel[2] > 180 && pixel[0] < 80;

    /// <summary>
    /// A structurally valid PNG that declares the given size, 8-bit RGBA, over an empty IDAT.
    /// Tiny on disk; enormous once decoded.
    /// </summary>
    private static byte[] CraftPng(int width, int height, byte bitDepth = 8, byte colorType = 6)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        WriteChunk(stream, "IHDR"u8, ihdr);

        // zlib header plus an empty final stored block.
        WriteChunk(stream, "IDAT"u8, [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(stream, "IEND"u8, []);

        return stream.ToArray();
    }

    /// <summary>
    /// A two-frame APNG, assembled from a real single-frame PNG's IDAT payload plus the acTL,
    /// fcTL and fdAT chunks that make it animated. libvips cannot write one, so the chunks are
    /// laid out here rather than checking a binary fixture into the repository.
    /// </summary>
    private static byte[] CraftApng()
    {
        using var source = TestImages.Solid(8, 8, 255, 0, 0);
        var png = TestImages.Png(source);

        var idat = new List<byte>();
        var ihdr = Array.Empty<byte>();
        var offset = 8;
        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length).ToArray();

            if (type == "IHDR")
                ihdr = data;
            if (type == "IDAT")
                idat.AddRange(data);

            offset += 12 + length;
        }

        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(stream, "IHDR"u8, ihdr);

        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl.AsSpan(0), 2); // frame count
        BinaryPrimitives.WriteUInt32BigEndian(actl.AsSpan(4), 0); // play count: infinite
        WriteChunk(stream, "acTL"u8, actl);

        WriteChunk(stream, "fcTL"u8, FrameControl(sequence: 0));
        WriteChunk(stream, "IDAT"u8, idat.ToArray());
        WriteChunk(stream, "fcTL"u8, FrameControl(sequence: 1));

        var fdat = new byte[4 + idat.Count];
        BinaryPrimitives.WriteUInt32BigEndian(fdat.AsSpan(0), 2);
        idat.CopyTo(fdat, 4);
        WriteChunk(stream, "fdAT"u8, fdat);
        WriteChunk(stream, "IEND"u8, []);

        return stream.ToArray();
    }

    private static byte[] FrameControl(uint sequence)
    {
        var fctl = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(0), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(4), 8);   // width
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(8), 8);   // height
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(12), 0);  // x offset
        BinaryPrimitives.WriteUInt32BigEndian(fctl.AsSpan(16), 0);  // y offset
        BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20), 1);  // delay numerator
        BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22), 10); // delay denominator
        fctl[24] = 0; // dispose operation
        fctl[25] = 0; // blend operation
        return fctl;
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);

        var typeAndData = new byte[type.Length + data.Length];
        type.CopyTo(typeAndData);
        data.CopyTo(typeAndData.AsSpan(type.Length));
        stream.Write(typeAndData);

        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32(typeAndData));
        stream.Write(buffer);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    /// <summary>A forward-only stream, like the body of a streamed multipart upload.</summary>
    private sealed class ForwardOnlyStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }

    /// <summary>A stream that faults part-way through, standing in for a broken disk buffer.</summary>
    private sealed class FailingStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(Span<byte> buffer) => throw new IOException("disk buffer went away");

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            throw new IOException("disk buffer went away");
    }

    /// <summary>Holds its processing slot by taking its time to be read.</summary>
    private sealed class SlowStream(byte[] content, TimeSpan delay) : MemoryStream(content)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
