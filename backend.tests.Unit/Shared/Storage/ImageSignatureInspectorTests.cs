using backend.main.shared.storage;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

namespace backend.tests.Unit.Shared.Storage;

public class ImageSignatureInspectorTests
{
    public static TheoryData<byte[], ImageFormat, string, string> RecognisedSignatures =>
        new()
        {
            // A complete JFIF header.
            {
                [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01],
                ImageFormat.Jpeg, "image/jpeg", ".jpg"
            },
            // Four bytes is enough for JPEG: the signature itself is only three.
            { [0xFF, 0xD8, 0xFF, 0xDB], ImageFormat.Jpeg, "image/jpeg", ".jpg" },
            {
                [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D],
                ImageFormat.Png, "image/png", ".png"
            },
            { "GIF87a"u8.ToArray(), ImageFormat.Gif, "image/gif", ".gif" },
            { "GIF89a"u8.ToArray(), ImageFormat.Gif, "image/gif", ".gif" },
            { Riff("WEBP"u8), ImageFormat.Webp, "image/webp", ".webp" }
        };

    public static TheoryData<byte[]> UnrecognisedHeaders =>
        new()
        {
            Array.Empty<byte>(),
            // A JPEG signature one byte short.
            new byte[] { 0xFF, 0xD8 },
            // A PNG signature one byte short.
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A },
            // RIFF with its size but no form type: the truncated (<12 byte) case.
            RiffWithoutFormType(),
            // RIFF that is a WAV, not a WebP.
            Riff("WAVE"u8),
            // An SVG-prefixed polyglot.
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>"u8.ToArray(),
            "%PDF-1.4"u8.ToArray(),
            "MZ"u8.ToArray(),
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            new byte[] { 0x01, 0x02, 0x03, 0x04 }
        };

    [Theory]
    [MemberData(nameof(RecognisedSignatures))]
    public void TryDetect_ShouldIdentifySupportedFormats(
        byte[] header,
        ImageFormat expectedFormat,
        string expectedContentType,
        string expectedExtension)
    {
        ImageSignatureInspector.TryDetect(header, out var signature).Should().BeTrue();

        signature.Format.Should().Be(expectedFormat);
        signature.ContentType.Should().Be(expectedContentType);
        signature.FileExtension.Should().Be(expectedExtension);
    }

    [Theory]
    [MemberData(nameof(UnrecognisedHeaders))]
    public void TryDetect_ShouldRejectAnythingElse(byte[] header)
    {
        ImageSignatureInspector.TryDetect(header, out var signature).Should().BeFalse();

        signature.Should().Be(default(ImageSignature));
    }

    [Fact]
    public void TryDetect_ShouldAcceptAPolyglotThatReallyIsAnImage()
    {
        // Documents the boundary of this layer rather than asserting a defence. Magic bytes prove
        // a file is not "not an image"; they cannot prove it is only an image. A payload behind a
        // genuine GIF89a header is a genuine GIF and is stored as one. Rejecting it needs full
        // decode and re-encode, which is the separately tracked media-worker work.
        var polyglot = (byte[])[.. "GIF89a"u8, .. "<script>alert(1)</script>"u8];

        ImageSignatureInspector.TryDetect(polyglot, out var signature).Should().BeTrue();

        signature.Format.Should().Be(ImageFormat.Gif);
        signature.ContentType.Should().Be("image/gif");
    }

    [Fact]
    public void TryDetect_ShouldRestoreTheStreamPositionItWasGiven()
    {
        using var stream = new MemoryStream(PngBytes());
        stream.Position = 4;

        ImageSignatureInspector.TryDetect(stream, out var signature).Should().BeTrue();

        signature.Format.Should().Be(ImageFormat.Png);
        stream.Position.Should().Be(4);
    }

    [Fact]
    public void TryDetect_ShouldTolerateShortReads()
    {
        // ReadAtLeast is expected to keep reading until the header is filled; a stream that hands
        // back one byte at a time proves we are not relying on a single Read returning everything.
        using var stream = new OneByteAtATimeStream(PngBytes());

        ImageSignatureInspector.TryDetect(stream, out var signature).Should().BeTrue();

        signature.Format.Should().Be(ImageFormat.Png);
    }

    [Fact]
    public void TryDetect_ShouldRefuseNonSeekableStreams_WithoutConsumingThem()
    {
        // Sniffing a stream we cannot rewind would leave the upload to store a blob missing the
        // inspected bytes, so the header must still be there for whoever reads next.
        using var inner = new MemoryStream(PngBytes());
        using var stream = new NonSeekableStream(inner);

        ImageSignatureInspector.TryDetect(stream, out var signature).Should().BeFalse();

        signature.Should().Be(default(ImageSignature));
        inner.Position.Should().Be(0);
    }

    [Fact]
    public void TryDetect_ShouldReturnFalse_ForNullOrUnreadableStreams()
    {
        ImageSignatureInspector.TryDetect((Stream)null!, out _).Should().BeFalse();

        using var writeOnly = new WriteOnlyStream();
        ImageSignatureInspector.TryDetect(writeOnly, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDetect_ShouldIgnoreTheFileNameAndTheDeclaredContentType()
    {
        var file = CreateFormFile("avatar.jpg", PngBytes(), "image/jpeg");

        ImageSignatureInspector.TryDetect(file, out var signature).Should().BeTrue();

        signature.Format.Should().Be(ImageFormat.Png);
        signature.ContentType.Should().Be("image/png");
        signature.FileExtension.Should().Be(".png");
    }

    [Fact]
    public void TryDetect_ShouldReturnFalse_ForNullOrEmptyFiles()
    {
        ImageSignatureInspector.TryDetect((IFormFile)null!, out _).Should().BeFalse();

        var empty = CreateFormFile("avatar.png", [], "image/png");
        ImageSignatureInspector.TryDetect(empty, out _).Should().BeFalse();
    }

    [Fact]
    public void HeaderByteCount_ShouldCoverTheLongestSignature()
    {
        // "RIFF" + four size bytes + "WEBP".
        ImageSignatureInspector.HeaderByteCount.Should().Be(12);
    }

    private static byte[] Riff(ReadOnlySpan<byte> formType) =>
        [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. formType];

    private static byte[] RiffWithoutFormType() =>
        [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00];

    private static byte[] PngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static FormFile CreateFormFile(string fileName, byte[] content, string? contentType = null)
    {
        var stream = new MemoryStream(content);
        var file = new FormFile(stream, 0, content.Length, "image", fileName)
        {
            Headers = new HeaderDictionary()
        };

        if (contentType is not null)
        {
            file.ContentType = contentType;
        }

        return file;
    }

    private sealed class OneByteAtATimeStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(Span<byte> buffer) =>
            buffer.IsEmpty ? 0 : base.Read(buffer[..1]);

        public override int Read(byte[] buffer, int offset, int count) =>
            count == 0 ? 0 : base.Read(buffer, offset, 1);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
