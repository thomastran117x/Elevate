namespace backend.main.shared.storage.imaging
{
    /// <summary>
    /// Decodes an uploaded image to pixels and re-encodes it, so nothing the uploader put in the
    /// file other than pixel data — EXIF, GPS, trailing script, a second frame — survives.
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// Validates, orients, resizes, strips and re-encodes <paramref name="source"/> to WebP.
        /// </summary>
        /// <param name="source">A readable, seekable stream positioned anywhere; it is read from the start.</param>
        /// <param name="profile">Which size cap applies.</param>
        /// <param name="cancellationToken">Cancels waiting for a processing slot and decoding.</param>
        /// <exception cref="exceptions.http.BadRequestException">
        /// The bytes are not a supported image format, or the image is too large, animated, or
        /// cannot be decoded. Everything the uploader can get wrong is a 400, which is what the
        /// avatar endpoint already returns from <c>[ImageContent]</c> model validation and what
        /// the published API contract promises.
        /// </exception>
        Task<ProcessedImage> ProcessAsync(
            Stream source,
            ImageProcessingProfile profile,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Selects the output size cap from <see cref="ImageProcessingOptions"/>.</summary>
    public enum ImageProcessingProfile
    {
        Avatar,
        Gallery
    }

    /// <summary>
    /// A re-encoded image ready to store. Always WebP, whatever format came in.
    /// </summary>
    public sealed record ProcessedImage(byte[] Content, int Width, int Height)
    {
        public const string WebpContentType = "image/webp";
        public const string WebpFileExtension = ".webp";

        public string ContentType => WebpContentType;

        public string FileExtension => WebpFileExtension;
    }
}
