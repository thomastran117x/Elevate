using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;

using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

using FluentAssertions;

using Microsoft.Extensions.Options;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.PixelFormats;

namespace backend.tests.Unit.Shared.Storage.Imaging;

/// <remarks>
/// Every fixture is generated here with ImageSharp or assembled byte by byte, so no binary assets
/// live in the repository.
/// </remarks>
public class ImageSharpImageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldRemoveGpsExif_AndOutputWebp()
    {
        using var input = new Image<Rgba32>(64, 48, new Rgba32(120, 160, 200));
        input.Metadata.ExifProfile = new ExifProfile();
        input.Metadata.ExifProfile.SetValue(ExifTag.GPSLatitudeRef, "N");
        input.Metadata.ExifProfile.SetValue(
            ExifTag.GPSLatitude,
            [new Rational(43, 1), new Rational(39, 1), new Rational(12, 1)]);
        var jpeg = EncodeJpeg(input);
        Image.Identify(jpeg).Metadata.ExifProfile.Should().NotBeNull("the fixture has to carry GPS to prove anything");

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        ImageSignatureInspector.TryDetect(result.Content, out var signature).Should().BeTrue();
        signature.Format.Should().Be(ImageFormat.Webp);
        result.ContentType.Should().Be("image/webp");
        result.FileExtension.Should().Be(".webp");

        var stored = Image.Identify(result.Content);
        stored.Metadata.ExifProfile.Should().BeNull();
        stored.Metadata.XmpProfile.Should().BeNull();
        stored.Metadata.IccProfile.Should().BeNull();
        stored.Metadata.IptcProfile.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_ShouldCapAvatarAtLongEdge512()
    {
        using var input = new Image<Rgba32>(4000, 3000, new Rgba32(10, 20, 30));
        var jpeg = EncodeJpeg(input);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(jpeg), ImageProcessingProfile.Avatar);

        result.Width.Should().Be(512);
        result.Height.Should().Be(384);
        var stored = Image.Identify(result.Content);
        stored.Width.Should().Be(512);
        stored.Height.Should().Be(384);
    }

    [Fact]
    public async Task ProcessAsync_ShouldCapGalleryAtLongEdge2048()
    {
        using var input = new Image<Rgba32>(1500, 3000, new Rgba32(10, 20, 30));
        var png = EncodePng(input);

        var result = await CreateProcessor().ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Gallery);

        result.Width.Should().Be(1024);
        result.Height.Should().Be(2048);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotUpscaleSmallImages()
    {
        using var input = new Image<Rgba32>(100, 80, new Rgba32(10, 20, 30));

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(EncodePng(input)), ImageProcessingProfile.Avatar);

        result.Width.Should().Be(100);
        result.Height.Should().Be(80);
    }

    [Fact]
    public async Task ProcessAsync_ShouldApplyExifOrientationBeforeStrippingIt()
    {
        // Stored landscape, red on the left and blue on the right, tagged orientation 6: "rotate
        // 90 degrees clockwise to display", which is how phones save portrait shots. Displayed
        // upright it is portrait with red on top. Stripping EXIF without applying the tag first
        // would store it landscape.
        using var input = new Image<Rgba32>(80, 40);
        input.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = x < row.Length / 2 ? new Rgba32(255, 0, 0) : new Rgba32(0, 0, 255);
            }
        });
        input.Metadata.ExifProfile = new ExifProfile();
        input.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(EncodeJpeg(input)), ImageProcessingProfile.Avatar);

        result.Width.Should().Be(40);
        result.Height.Should().Be(80);

        using var stored = Image.Load<Rgba32>(result.Content);
        stored.Metadata.ExifProfile.Should().BeNull();
        IsMostly(stored[20, 15], red: true).Should().BeTrue("the top half should be the red side");
        IsMostly(stored[20, 65], red: false).Should().BeTrue("the bottom half should be the blue side");
    }

    [Fact]
    public async Task ProcessAsync_ShouldStillOrientCorrectly_WhenTheImageIsShrunkFirst()
    {
        // Resizing happens before AutoOrient, so a full-resolution rotation never allocates a
        // second full-size buffer. The EXIF tag has to survive the resize for this to work.
        using var input = new Image<Rgba32>(2000, 1000);
        input.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = x < row.Length / 2 ? new Rgba32(255, 0, 0) : new Rgba32(0, 0, 255);
            }
        });
        input.Metadata.ExifProfile = new ExifProfile();
        input.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(EncodeJpeg(input)), ImageProcessingProfile.Avatar);

        result.Width.Should().Be(256);
        result.Height.Should().Be(512);

        using var stored = Image.Load<Rgba32>(result.Content);
        IsMostly(stored[128, 64], red: true).Should().BeTrue("the top half should be the red side");
        IsMostly(stored[128, 448], red: false).Should().BeTrue("the bottom half should be the blue side");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectADecompressionBombFromItsHeaderAlone()
    {
        // 100000 x 100000 RGBA is 40 GB of pixels declared by a file of about 70 bytes. Were the
        // header probe skipped, the decode would try to allocate it and fail on memory, with a
        // different message, and allocate far more than this test allows.
        var processor = CreateProcessor();
        var bomb = CraftPng(100_000, 100_000);

        // Warm up so JIT and ImageSharp's one-time static setup are not counted against the probe.
        await processor.Invoking(p => p.ProcessAsync(new MemoryStream(bomb), ImageProcessingProfile.Avatar))
            .Should().ThrowAsync<BadRequestException>();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var act = () => processor.ProcessAsync(new MemoryStream(bomb), ImageProcessingProfile.Avatar);
        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.TooLargeMessage);
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
            .WithMessage(ImageSharpImageProcessor.TooLargeMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAnimatedGif()
    {
        using var input = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0));
        using (var second = new Image<Rgba32>(16, 16, new Rgba32(0, 0, 255)))
        {
            input.Frames.AddFrame(second.Frames.RootFrame);
        }

        using var gif = new MemoryStream();
        input.SaveAsGif(gif);
        gif.Position = 0;

        var act = () => CreateProcessor().ProcessAsync(gif, ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldConvertStaticGifToWebp()
    {
        using var input = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0));
        using var gif = new MemoryStream();
        input.SaveAsGif(gif);
        gif.Position = 0;

        var result = await CreateProcessor().ProcessAsync(gif, ImageProcessingProfile.Avatar);

        ImageSignatureInspector.TryDetect(result.Content, out var signature).Should().BeTrue();
        signature.Format.Should().Be(ImageFormat.Webp);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectBytesThatAreNotAnImage()
    {
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream([0x01, 0x02, 0x03, 0x04]), ImageProcessingProfile.Avatar);

        // 400, not 415: [ImageContent] model validation already answers 400 for the same file,
        // and the published contract documents that.
        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.UnsupportedFormatMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectFormatsOutsideTheAllowlist()
    {
        using var input = new Image<Rgba32>(8, 8);
        using var bmp = new MemoryStream();
        input.SaveAsBmp(bmp);
        bmp.Position = 0;

        var act = () => CreateProcessor().ProcessAsync(bmp, ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.UnsupportedFormatMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAHeaderThatCannotBeDecoded()
    {
        // A PNG signature followed by nothing a decoder can use.
        byte[] corrupt = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48];

        var act = () => CreateProcessor().ProcessAsync(new MemoryStream(corrupt), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.UnreadableMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldQueueRequestsBeyondTheConcurrencyLimit()
    {
        var processor = CreateProcessor(new ImageProcessingOptions { MaxConcurrentOperations = 1 });
        using var input = new Image<Rgba32>(32, 32, new Rgba32(1, 2, 3));
        var png = EncodePng(input);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar))));

        results.Should().AllSatisfy(result => result.Width.Should().Be(32));
    }

    [Fact]
    public async Task ProcessAsync_WhenCancelled_ShouldThrowAndLeaveTheSlotFree()
    {
        var processor = CreateProcessor(new ImageProcessingOptions { MaxConcurrentOperations = 1 });
        using var input = new Image<Rgba32>(32, 32, new Rgba32(1, 2, 3));
        var png = EncodePng(input);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var cancelled = () => processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar, cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        // With a single slot, a leaked one would make this wait forever.
        var next = processor.ProcessAsync(new MemoryStream(png), ImageProcessingProfile.Avatar);
        (await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(next);
        (await next).Width.Should().Be(32);
    }

    [Fact]
    public async Task ProcessAsync_ShouldStripEveryMetadataProfile_EvenWhenTheInputCarriesThemAll()
    {
        // The encoder's SkipMetadata is the only mechanism that drops metadata, so this asserts
        // the encoded output directly rather than trusting an in-memory clean-up step.
        using var input = new Image<Rgba32>(40, 30, new Rgba32(90, 140, 190));
        input.Metadata.ExifProfile = new ExifProfile();
        input.Metadata.ExifProfile.SetValue(ExifTag.Software, "definitely-not-wanted");
        input.Metadata.IptcProfile = new IptcProfile();
        input.Metadata.IptcProfile.SetValue(IptcTag.Byline, "someone");

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(EncodeJpeg(input)), ImageProcessingProfile.Avatar);

        var stored = Image.Identify(result.Content);
        stored.Metadata.ExifProfile.Should().BeNull();
        stored.Metadata.XmpProfile.Should().BeNull();
        stored.Metadata.IptcProfile.Should().BeNull();
        stored.Metadata.IccProfile.Should().BeNull();
        result.Content.AsSpan().IndexOf("definitely-not-wanted"u8).Should().Be(-1, "the EXIF string must not survive anywhere in the encoded bytes");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectAnimatedWebp()
    {
        using var input = new Image<Rgba32>(32, 32, new Rgba32(255, 0, 0));
        using (var second = new Image<Rgba32>(32, 32, new Rgba32(0, 0, 255)))
        {
            input.Frames.AddFrame(second.Frames.RootFrame);
        }

        using var webp = new MemoryStream();
        input.Save(webp, new WebpEncoder());
        webp.Position = 0;

        var act = () => CreateProcessor().ProcessAsync(webp, ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectApngAsAnimated_NotAsUnreadable()
    {
        // ImageSharp 3.x cannot decode an APNG at all: left to the decoder it fails as invalid
        // PNG data, which would tell the user their file is broken rather than animated. The
        // acTL chunk is checked before decoding so the message matches GIF and WebP.
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(CraftApng()), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.AnimatedMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptAStillPngWhateverItsPixelBytesContain()
    {
        // Guards the acTL scan against matching bytes inside pixel data: the scan walks chunk
        // headers and stops at IDAT rather than searching the whole file.
        using var input = new Image<Rgba32>(64, 64);
        input.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)(x * 7), (byte)(y * 5), (byte)(x ^ y));
            }
        });

        var result = await CreateProcessor().ProcessAsync(
            new MemoryStream(EncodePng(input)), ImageProcessingProfile.Avatar);

        result.Width.Should().Be(64);
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
            .WithMessage(ImageSharpImageProcessor.UnreadableMessage);
    }

    [Theory]
    [InlineData((byte)8, (byte)5)]   // colour type 5 does not exist
    [InlineData((byte)3, (byte)6)]   // bit depth 3 is not valid for RGBA
    public async Task ProcessAsync_ShouldReturnBadRequest_ForUnsupportedPngVariants(byte bitDepth, byte colorType)
    {
        // ImageSharp raises NotSupportedException rather than an ImageFormatException for these,
        // which would otherwise escape the filter and surface as a 500.
        var act = () => CreateProcessor().ProcessAsync(
            new MemoryStream(CraftPng(8, 8, bitDepth, colorType)), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage(ImageSharpImageProcessor.UnreadableMessage);
    }

    [Fact]
    public async Task ProcessAsync_ShouldAcceptAStreamThatCannotSeek()
    {
        // The upload is buffered once up front, so a forward-only stream is fine.
        using var input = new Image<Rgba32>(24, 24, new Rgba32(7, 8, 9));
        var png = EncodePng(input);

        var result = await CreateProcessor().ProcessAsync(
            new ForwardOnlyStream(png), ImageProcessingProfile.Avatar);

        result.Width.Should().Be(24);
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

    private static ImageSharpImageProcessor CreateProcessor(ImageProcessingOptions? options = null) =>
        new(Options.Create(options ?? new ImageProcessingOptions()));

    private static byte[] EncodeJpeg(Image image)
    {
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static byte[] EncodePng(Image image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static bool IsMostly(Rgba32 pixel, bool red) =>
        red
            ? pixel.R > 180 && pixel.B < 80
            : pixel.B > 180 && pixel.R < 80;

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
    /// fcTL and fdAT chunks that make it animated. ImageSharp 3.x cannot write one, so the chunks
    /// are laid out here rather than checking a binary fixture into the repository.
    /// </summary>
    private static byte[] CraftApng()
    {
        using var source = new Image<Rgba32>(8, 8, new Rgba32(255, 0, 0));
        var png = EncodePng(source);

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
}
