using System.ComponentModel.DataAnnotations;

namespace backend.main.shared.storage.imaging
{
    /// <summary>
    /// Limits and output settings for the decode-and-re-encode pipeline in
    /// <see cref="NetVipsImageProcessor"/>, bound from the <c>ImageProcessing</c> section.
    /// </summary>
    public sealed class ImageProcessingOptions
    {
        /// <summary>
        /// Largest width or height accepted, read from the header before any pixel buffer exists.
        /// </summary>
        [Range(1, 65535)]
        public int MaxDimension { get; set; } = 8000;

        /// <summary>
        /// Largest total pixel count accepted. Each side can be within <see cref="MaxDimension"/>
        /// while the product still decodes to hundreds of megabytes, so both are checked.
        /// </summary>
        [Range(1L, 1_000_000_000L)]
        public long MaxPixels { get; set; } = 50_000_000;

        /// <summary>Long-edge cap for avatars. Smaller images are never upscaled.</summary>
        [Range(16, 8000)]
        public int AvatarMaxEdge { get; set; } = 512;

        /// <summary>
        /// Long-edge cap for event and club images uploaded through a presigned URL. Smaller
        /// images are never upscaled. 2048 keeps a full-width banner sharp and stays inside the
        /// input limits of the image-moderation API that screens these later.
        /// </summary>
        [Range(16, 8000)]
        public int GalleryMaxEdge { get; set; } = 2048;

        /// <summary>Lossy WebP quality, 1-100.</summary>
        [Range(1, 100)]
        public int WebpQuality { get; set; } = 82;

        /// <summary>
        /// Images processed at once across the process; further requests wait. One image at the
        /// <see cref="MaxPixels"/> limit can hold its fully decoded pixels (up to 4 bytes each, so
        /// ~200 MB at 50 MP) plus the buffered upload while it is shrunk, and this multiplies that
        /// peak: at the defaults, budget roughly 512 MB of headroom for image processing alone.
        /// </summary>
        [Range(1, 64)]
        public int MaxConcurrentOperations { get; set; } = 2;

        /// <summary>
        /// How long an upload waits for a free slot before the request is shed with 503. Nothing
        /// else bounds that queue — the rate limit is per account — so without this, uploads from
        /// enough accounts pile up, each holding its buffered body until the client gives up.
        /// </summary>
        [Range(1, 120)]
        public int SlotWaitTimeoutSeconds { get; set; } = 10;
    }
}
