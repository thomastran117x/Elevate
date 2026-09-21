using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using ImageSharpConfiguration = SixLabors.ImageSharp.Configuration;

namespace backend.main.shared.storage.imaging
{
    /// <summary>
    /// <see cref="IImageProcessor"/> on ImageSharp, which is fully managed: a hostile file
    /// surfaces as a catchable exception rather than a native allocation failure that takes the
    /// process down with it.
    /// </summary>
    public sealed class ImageSharpImageProcessor : IImageProcessor
    {
        internal const string UnsupportedFormatMessage = "Only JPEG, PNG, WEBP, and GIF images are supported.";
        internal const string TooLargeMessage = "The image dimensions are too large.";
        internal const string AnimatedMessage = "Animated images are not supported. Upload a single-frame image.";
        internal const string UnreadableMessage = "The image could not be read.";

        private readonly ImageProcessingOptions _options;
        private readonly DecoderOptions _identifyOptions;
        private readonly DecoderOptions _decodeOptions;
        private readonly WebpEncoder _encoder;
        private readonly SemaphoreSlim _slots;

        public ImageSharpImageProcessor(IOptions<ImageProcessingOptions> options)
        {
            _options = options.Value;

            // Only the formats ImageSignatureInspector admits. The default configuration also
            // decodes BMP, TIFF, TGA, PBM, QOI and ICO: parsers nobody here needs, each one more
            // place for a malformed file to find a bug.
            var configuration = new ImageSharpConfiguration(
                new JpegConfigurationModule(),
                new PngConfigurationModule(),
                new WebpConfigurationModule(),
                new GifConfigurationModule())
            {
                MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
                {
                    AllocationLimitMegabytes = _options.MaxAllocationMegabytes
                })
            };

            _identifyOptions = new DecoderOptions { Configuration = configuration };
            _decodeOptions = new DecoderOptions { Configuration = configuration, MaxFrames = 1 };

            // SkipMetadata keeps EXIF, XMP and ICC out of the output even if a profile survived
            // the explicit clearing below. ImageSharp 3.x cannot convert an ICC profile to sRGB,
            // so a wide-gamut photo loses a little saturation; that is the price of not carrying
            // arbitrary profile bytes through to a public URL.
            _encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = _options.WebpQuality,
                SkipMetadata = true
            };

            _slots = new SemaphoreSlim(_options.MaxConcurrentOperations, _options.MaxConcurrentOperations);
        }

        public async Task<ProcessedImage> ProcessAsync(
            Stream source,
            ImageProcessingProfile profile,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!source.CanRead || !source.CanSeek)
                throw new ArgumentException("The image stream must be readable and seekable.", nameof(source));

            // The cheap byte check first: it keeps anything that is not one of the four formats
            // away from every decoder, and it is the same gate the presigned path applies.
            if (!ImageSignatureInspector.TryDetect(source, out _))
                throw new UnsupportedMediaTypeException(UnsupportedFormatMessage);

            await _slots.WaitAsync(cancellationToken);
            try
            {
                return await DecodeAndEncodeAsync(source, MaxEdgeFor(profile), cancellationToken);
            }
            catch (Exception ex) when (ex is ImageFormatException or InvalidMemoryOperationException)
            {
                // UnknownImageFormatException and InvalidImageContentException both derive from
                // ImageFormatException. A file the decoder cannot read is the uploader's problem,
                // never a 500.
                Logger.Warn(ex, "[ImageSharpImageProcessor] Rejected an image that could not be decoded.");
                throw new BadRequestException(UnreadableMessage);
            }
            finally
            {
                _slots.Release();
            }
        }

        private async Task<ProcessedImage> DecodeAndEncodeAsync(
            Stream source,
            int maxEdge,
            CancellationToken cancellationToken)
        {
            // 1. Header only. Identify reads dimensions and frame descriptors without allocating
            // a pixel buffer, so a 100 KB file declaring 100000x100000 is refused here, before
            // it can ask for 40 GB.
            source.Position = 0;
            var info = await Image.IdentifyAsync(_identifyOptions, source, cancellationToken);

            if (info.Width > _options.MaxDimension ||
                info.Height > _options.MaxDimension ||
                (long)info.Width * info.Height > _options.MaxPixels)
            {
                throw new BadRequestException(TooLargeMessage);
            }

            // An animated GIF, WebP or APNG is a frame-count bomb, and flattening it to its first
            // frame silently destroys what the user meant to upload. Refusing says so.
            if (info.FrameMetadataCollection.Count > 1)
                throw new BadRequestException(AnimatedMessage);

            // 2. Full decode, capped at one frame and bounded by the configured allocator.
            source.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(_decodeOptions, source, cancellationToken);

            // 3. Shrink first, then orient. Rotating at full resolution would allocate a second
            // full-size buffer — another ~200 MB for a 50 MP photo — before the original is freed.
            // The cap is a square box, so the result is the same in either order, and Resize
            // keeps the EXIF profile, so AutoOrient still sees the tag afterwards.
            if (Math.Max(image.Width, image.Height) > maxEdge)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxEdge, maxEdge)
                }));
            }

            // ImageSharp does not apply the EXIF orientation tag on load, and the tag lives in the
            // EXIF profile cleared below, so skipping this would store every portrait phone photo
            // on its side.
            image.Mutate(context => context.AutoOrient());

            // 4. Strip. Only pixels leave this method.
            StripMetadata(image);

            // 5. Always WebP: one encoder path, and a format the uploader did not choose.
            using var output = new MemoryStream();
            await _encoder.EncodeAsync(image, output, cancellationToken);

            return new ProcessedImage(output.ToArray(), image.Width, image.Height);
        }

        private int MaxEdgeFor(ImageProcessingProfile profile) => profile switch
        {
            ImageProcessingProfile.Avatar => _options.AvatarMaxEdge,
            ImageProcessingProfile.Gallery => _options.GalleryMaxEdge,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
        };

        private static void StripMetadata(Image image)
        {
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IccProfile = null;

            foreach (var frame in image.Frames)
            {
                frame.Metadata.ExifProfile = null;
                frame.Metadata.IptcProfile = null;
                frame.Metadata.XmpProfile = null;
                frame.Metadata.IccProfile = null;
            }
        }
    }
}
