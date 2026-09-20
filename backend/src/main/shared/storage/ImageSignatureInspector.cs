namespace backend.main.shared.storage
{
    /// <summary>
    /// An image format this application is willing to store.
    /// </summary>
    public enum ImageFormat
    {
        Jpeg,
        Png,
        Webp,
        Gif
    }

    /// <summary>
    /// The content type and file extension derived from a file's own leading bytes.
    /// </summary>
    public readonly record struct ImageSignature(
        ImageFormat Format,
        string ContentType,
        string FileExtension);

    /// <summary>
    /// Identifies an image by its leading bytes. The declared Content-Type and the file name are
    /// both chosen by whoever is uploading, so neither can be allowed to decide what a publicly
    /// readable blob is served as; only the bytes can.
    /// </summary>
    public static class ImageSignatureInspector
    {
        /// <summary>
        /// WEBP carries the longest signature: "RIFF", four size bytes, then "WEBP".
        /// </summary>
        public const int HeaderByteCount = 12;

        private static ReadOnlySpan<byte> PngMagic =>
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private static ReadOnlySpan<byte> Gif87aMagic => "GIF87a"u8;

        private static ReadOnlySpan<byte> Gif89aMagic => "GIF89a"u8;

        private static ReadOnlySpan<byte> RiffMagic => "RIFF"u8;

        private static ReadOnlySpan<byte> WebpMagic => "WEBP"u8;

        /// <summary>
        /// Matches the leading bytes of a file against the supported image signatures. A header
        /// shorter than a given signature simply fails to match it, so truncated input is
        /// rejected rather than read past.
        /// </summary>
        public static bool TryDetect(ReadOnlySpan<byte> header, out ImageSignature signature)
        {
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                signature = new ImageSignature(ImageFormat.Jpeg, "image/jpeg", ".jpg");
                return true;
            }

            if (header.Length >= PngMagic.Length && header[..PngMagic.Length].SequenceEqual(PngMagic))
            {
                signature = new ImageSignature(ImageFormat.Png, "image/png", ".png");
                return true;
            }

            if (header.Length >= Gif87aMagic.Length &&
                (header[..Gif87aMagic.Length].SequenceEqual(Gif87aMagic) ||
                 header[..Gif89aMagic.Length].SequenceEqual(Gif89aMagic)))
            {
                signature = new ImageSignature(ImageFormat.Gif, "image/gif", ".gif");
                return true;
            }

            // RIFF is a container format: WAV and AVI open with the same four bytes, so the form
            // type at offset 8 is the part that actually says "this is a WebP".
            if (header.Length >= HeaderByteCount &&
                header[..RiffMagic.Length].SequenceEqual(RiffMagic) &&
                header.Slice(8, WebpMagic.Length).SequenceEqual(WebpMagic))
            {
                signature = new ImageSignature(ImageFormat.Webp, "image/webp", ".webp");
                return true;
            }

            signature = default;
            return false;
        }

        /// <summary>
        /// Inspects the stream from its start and restores the position it was given, so the
        /// caller can hand the same stream straight to an upload. A stream that cannot seek is
        /// reported as unrecognised rather than consumed: reading its header would leave the
        /// upload to store a blob missing the bytes that were inspected.
        /// </summary>
        public static bool TryDetect(Stream stream, out ImageSignature signature)
        {
            signature = default;

            if (stream is null || !stream.CanRead || !stream.CanSeek)
            {
                return false;
            }

            var originalPosition = stream.Position;

            try
            {
                stream.Position = 0;
                Span<byte> header = stackalloc byte[HeaderByteCount];
                var read = stream.ReadAtLeast(header, HeaderByteCount, throwOnEndOfStream: false);
                return TryDetect(header[..read], out signature);
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        /// <summary>
        /// Inspects an uploaded file without consuming it. A zero-length file is never an image.
        /// </summary>
        public static bool TryDetect(IFormFile file, out ImageSignature signature)
        {
            signature = default;

            if (file is null || file.Length == 0)
            {
                return false;
            }

            using var stream = file.OpenReadStream();
            return TryDetect(stream, out signature);
        }
    }
}
