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
        /// <param name="source">
        /// A readable stream holding the whole upload. It is buffered once: from the start if it
        /// can seek, otherwise from its current position.
        /// </param>
        /// <param name="cancellationToken">Cancels waiting for a processing slot and decoding.</param>
        /// <exception cref="exceptions.http.BadRequestException">
        /// The bytes are not a supported image format, or the image is too large, animated, or
        /// cannot be decoded. Everything the uploader can get wrong is a 400, which is what the
        /// avatar endpoint already returns from <c>[ImageContent]</c> model validation and what
        /// the published API contract promises.
        /// </exception>
        /// <exception cref="exceptions.http.NotAvailableException">
        /// No processing slot became free within the configured wait.
        /// </exception>
        Task<ProcessedImage> ProcessAsync(Stream source, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A re-encoded image ready to store. Always WebP, whatever format came in.
    /// </summary>
    /// <remarks>
    /// The constructor is internal so that holding one of these means the bytes came out of the
    /// processor. <see cref="IAzureBlobService.UploadProcessedImageAsync"/> stores them in an
    /// anonymously readable container without sniffing them again, so a caller able to wrap raw
    /// upload bytes in this type could publish EXIF, GPS or a polyglot payload as image/webp.
    /// Within this assembly that is a convention rather than a guarantee; the compiler enforces it
    /// against every other one, and the tests reach it through InternalsVisibleTo.
    /// </remarks>
    public sealed record ProcessedImage
    {
        internal ProcessedImage(byte[] content) => Content = content;

        public byte[] Content
        {
            get;
        }

        public string ContentType => WebpMedia.ContentType;

        public string FileExtension => WebpMedia.FileExtension;
    }
}
