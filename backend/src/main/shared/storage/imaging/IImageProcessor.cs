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
        /// <param name="profile">
        /// What the image is for, which decides the long-edge cap. Required rather than defaulted
        /// so a new caller has to choose instead of silently inheriting the avatar size.
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
        Task<ProcessedImage> ProcessAsync(
            Stream source,
            ImageProcessingProfile profile,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// What a processed image will be used for. Each profile has its own long-edge cap in
    /// <see cref="ImageProcessingOptions"/>; every other step of the pipeline is shared.
    /// </summary>
    public enum ImageProcessingProfile
    {
        /// <summary>A profile picture, shown small: <see cref="ImageProcessingOptions.AvatarMaxEdge"/>.</summary>
        Avatar,

        /// <summary>
        /// An event or club image uploaded through a presigned URL — gallery tiles, covers, club
        /// icons and banners: <see cref="ImageProcessingOptions.GalleryMaxEdge"/>.
        /// </summary>
        Gallery
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
        internal ProcessedImage(byte[] content, int width = 0, int height = 0)
        {
            Content = content;
            Width = width;
            Height = height;
        }

        public byte[] Content
        {
            get;
        }

        /// <summary>Width of the encoded output in pixels, after resizing and orienting.</summary>
        public int Width
        {
            get;
        }

        /// <summary>Height of the encoded output in pixels, after resizing and orienting.</summary>
        public int Height
        {
            get;
        }

        public string ContentType => WebpMedia.ContentType;

        public string FileExtension => WebpMedia.FileExtension;
    }
}
