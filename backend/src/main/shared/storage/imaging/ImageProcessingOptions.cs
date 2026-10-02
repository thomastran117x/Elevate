using System.ComponentModel.DataAnnotations;

namespace backend.main.shared.storage.imaging
{
    /// <summary>
    /// Limits and output settings for the decode-and-re-encode pipeline in
    /// <see cref="ImageSharpImageProcessor"/>, bound from the <c>ImageProcessing</c> section.
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
        /// Largest <em>single</em> allocation the decoder may make, not a budget for one image.
        /// 50 MP of RGBA32 is one ~200 MB pixel buffer, but a decode also takes scratch buffers —
        /// spectral and colour buffers per component for a progressive JPEG, the resize target,
        /// the buffered upload — each counted separately against this limit. Peak resident memory
        /// for one 50 MP image is therefore several hundred megabytes, not 256 MB.
        /// </summary>
        [Range(16, 4096)]
        public int MaxAllocationMegabytes { get; set; } = 256;

        /// <summary>
        /// Bound on the allocator's internal buffer pool. Without it the pool keeps the largest
        /// image's buffers for the life of the process, so idle memory stays at the high-water
        /// mark of the worst upload ever handled.
        /// </summary>
        [Range(0, 4096)]
        public int MaxPoolMegabytes { get; set; } = 128;

        /// <summary>
        /// Images processed at once across the process; further requests wait. Multiply the
        /// per-image peak described on <see cref="MaxAllocationMegabytes"/> by this to size a
        /// container: at the defaults, budget roughly 1 GB of headroom for image processing alone.
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
