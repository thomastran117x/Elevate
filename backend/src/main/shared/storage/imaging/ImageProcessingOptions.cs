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
        /// Long-edge cap for gallery images. 2048 matches the largest image Azure AI Content Safety
        /// accepts, so moderating this pipeline's output never needs a second encode.
        /// </summary>
        [Range(16, 8000)]
        public int GalleryMaxEdge { get; set; } = 2048;

        /// <summary>Lossy WebP quality, 0-100.</summary>
        [Range(1, 100)]
        public int WebpQuality { get; set; } = 82;

        /// <summary>
        /// Largest single buffer the decoder may allocate. 50 MP of RGBA32 is about 200 MB, so the
        /// default admits anything that passes <see cref="MaxPixels"/> and nothing far beyond it.
        /// </summary>
        [Range(16, 4096)]
        public int MaxAllocationMegabytes { get; set; } = 256;

        /// <summary>
        /// Images processed at once across the process. The allocation limit bounds one image;
        /// this bounds how many of them can be resident together. Excess requests wait.
        /// </summary>
        [Range(1, 64)]
        public int MaxConcurrentOperations { get; set; } = 2;
    }
}
