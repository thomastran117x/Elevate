using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;

using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

using FluentAssertions;

using Microsoft.Extensions.Options;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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

        await act.Should().ThrowAsync<UnsupportedMediaTypeException>()
            .WithMessage("*JPEG, PNG, WEBP, and GIF*");
    }

    [Fact]
    public async Task ProcessAsync_ShouldRejectFormatsOutsideTheAllowlist()
    {
        using var input = new Image<Rgba32>(8, 8);
        using var bmp = new MemoryStream();
        input.SaveAsBmp(bmp);
        bmp.Position = 0;

        var act = () => CreateProcessor().ProcessAsync(bmp, ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<UnsupportedMediaTypeException>();
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
    public async Task ProcessAsync_ShouldRejectStreamsThatCannotSeek()
    {
        var act = () => CreateProcessor().ProcessAsync(new NonSeekableStream(), ImageProcessingProfile.Avatar);

        await act.Should().ThrowAsync<ArgumentException>();
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
    private static byte[] CraftPng(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: RGBA
        WriteChunk(stream, "IHDR"u8, ihdr);

        // zlib header plus an empty final stored block.
        WriteChunk(stream, "IDAT"u8, [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        WriteChunk(stream, "IEND"u8, []);

        return stream.ToArray();
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

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
