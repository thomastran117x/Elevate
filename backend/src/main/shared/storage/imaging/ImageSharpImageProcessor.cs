using System.Buffers.Binary;

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
        internal const string BusyMessage = "Image processing is busy. Try again shortly.";

        private readonly ImageProcessingOptions _options;
        private readonly DecoderOptions _identifyOptions;
        private readonly DecoderOptions _decodeOptions;
        private readonly WebpEncoder _encoder;
        private readonly SemaphoreSlim _slots;
        private readonly TimeSpan _slotWaitTimeout;

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
                    AllocationLimitMegabytes = _options.MaxAllocationMegabytes,

                    // The allocator pools buffers, and an unbounded pool keeps the high-water mark
                    // of the largest image resident for the life of the process. Bounding it keeps
                    // idle memory close to what a container is sized for.
                    MaximumPoolSizeMegabytes = _options.MaxPoolMegabytes
                })
            };

            _identifyOptions = new DecoderOptions { Configuration = configuration };
            _decodeOptions = new DecoderOptions { Configuration = configuration, MaxFrames = 1 };

            // SkipMetadata is the single place metadata is dropped: EXIF (GPS included), IPTC, XMP
            // and ICC are all left out of the encoded output. ImageSharp 3.x cannot convert an ICC
            // profile to sRGB, so a wide-gamut photo loses a little saturation; that is the price
            // of not carrying arbitrary profile bytes through to a public URL.
            _encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = _options.WebpQuality,
                SkipMetadata = true
            };

            _slots = new SemaphoreSlim(_options.MaxConcurrentOperations, _options.MaxConcurrentOperations);
            _slotWaitTimeout = TimeSpan.FromSeconds(_options.SlotWaitTimeoutSeconds);
        }

        public async Task<ProcessedImage> ProcessAsync(
            Stream source,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (!source.CanRead)
                throw new ArgumentException("The image stream must be readable.", nameof(source));

            // Everything that holds memory happens under the slot, buffering included. The request
            // limiter is a fixed window rather than a concurrency gate, so requests can queue here;
            // copying before taking a slot would let each one hold its own megabytes of upload
            // while only MaxConcurrentOperations of them are being worked on.
            //
            // The wait is bounded because that queue has no other limit: the rate limit is per
            // account, so enough accounts uploading at once would each hold a buffered body and a
            // request slot until the client gave up. Shedding load says so immediately instead.
            if (!await _slots.WaitAsync(_slotWaitTimeout, cancellationToken))
                throw new NotAvailableException(BusyMessage);

            try
            {
                // Buffer once. ImageSharp's async decoders copy a non-seekable or non-memory stream
                // into a scratch buffer on every call, so identifying and decoding the raw form
                // stream would copy the upload twice over.
                using var buffered = await BufferAsync(source, cancellationToken);

                // The cheap byte check first: it keeps anything that is not one of the four formats
                // away from every decoder, and it is the same gate the presigned path applies.
                if (!ImageSignatureInspector.TryDetect(buffered, out var signature))
                    throw new BadRequestException(UnsupportedFormatMessage);

                // ImageSharp 3.x cannot decode an APNG at all: it reports the file as invalid PNG
                // data, which would surface as "could not be read" rather than saying the image is
                // animated. The animation control chunk is what makes a PNG an APNG, so finding it
                // here keeps the message accurate and matches the GIF and WebP behaviour.
                if (signature.Format == ImageFormat.Png &&
                    HasApngAnimationChunk(buffered.GetBuffer().AsSpan(0, (int)buffered.Length)))
                {
                    throw new BadRequestException(AnimatedMessage);
                }

                return await DecodeAndEncodeAsync(buffered, cancellationToken);
            }
            catch (Exception ex) when (ex is not AppException and not OperationCanceledException)
            {
                // Deliberately broad. A crafted file reaches the decoders as whatever the parser
                // that choked on it happened to throw: ImageFormatException and NotSupportedException
                // are the documented ones, but ImageSharp 3.1 also surfaces IndexOutOfRange,
                // ArgumentOutOfRange, EndOfStream and InvalidOperation from malformed GIF, WebP and
                // JPEG streams. Every one of them is the uploader's file being wrong, never a fault
                // worth a 500, and listing types has already missed cases twice. Cancellation and
                // the rejections raised above are not decoder failures, so they pass through.
                Logger.Warn(ex, "[ImageSharpImageProcessor] Rejected an image that could not be decoded.");
                throw new BadRequestException(UnreadableMessage);
            }
            finally
            {
                _slots.Release();
            }
        }

        /// <remarks>
        /// The decoders are the token-aware overloads: a decode of an image at the pixel limit is
        /// long enough that checking cancellation only between stages would let a disconnected
        /// request hold its buffers and its slot to the end. They read the buffered MemoryStream
        /// directly, so the async path costs no extra copy (measured: ~200 bytes over the
        /// synchronous call for a 5 MB upload).
        /// </remarks>
        private async Task<ProcessedImage> DecodeAndEncodeAsync(
            MemoryStream source,
            CancellationToken cancellationToken)
        {
            var maxEdge = _options.AvatarMaxEdge;

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

            // An animated GIF or WebP is a frame-count bomb, and flattening it to its first frame
            // silently destroys what the user meant to upload. Refusing says so.
            if (info.FrameMetadataCollection.Count > 1)
                throw new BadRequestException(AnimatedMessage);

            // 2. Full decode, capped at one frame and bounded by the configured allocator.
            source.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(_decodeOptions, source, cancellationToken);

            // 3. Shrink first, then orient. Rotating at full resolution would allocate a second
            // full-size buffer — another ~200 MB for a 50 MP photo — before the original is freed.
            // The cap is a square box, so the result is the same in either order, and Resize
            // keeps the EXIF profile, so AutoOrient still sees the tag afterwards.
            cancellationToken.ThrowIfCancellationRequested();
            if (Math.Max(image.Width, image.Height) > maxEdge)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxEdge, maxEdge)
                }));
            }

            // ImageSharp does not apply the EXIF orientation tag on load, and the tag is dropped
            // with the rest of the metadata at encode time, so skipping this would store every
            // portrait phone photo on its side.
            image.Mutate(context => context.AutoOrient());

            // 4. Always WebP: one encoder path, a format the uploader did not choose, and
            // SkipMetadata means only pixels are written.
            using var output = new MemoryStream();
            await image.SaveAsync(output, _encoder, cancellationToken);

            return new ProcessedImage(output.ToArray());
        }

        /// <summary>
        /// Copies the upload into a single seekable buffer that every later step reads.
        /// </summary>
        private static async Task<MemoryStream> BufferAsync(Stream source, CancellationToken cancellationToken)
        {
            // Always a copy the caller does not own: this stream is disposed here, and the caller's
            // is not ours to close.
            var capacity = source.CanSeek ? checked((int)source.Length) : 0;
            var buffer = new MemoryStream(capacity);

            if (source.CanSeek)
                source.Position = 0;

            await source.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }

        /// <summary>
        /// Reports whether a PNG carries the <c>acTL</c> animation control chunk, which is what
        /// distinguishes an APNG from a still PNG. Only the chunk headers before the first
        /// <c>IDAT</c> are walked, so this reads a few dozen bytes rather than the pixel data.
        /// </summary>
        private static bool HasApngAnimationChunk(ReadOnlySpan<byte> png)
        {
            const int SignatureLength = 8;
            const int ChunkHeaderLength = 8;

            var offset = SignatureLength;
            while (offset + ChunkHeaderLength <= png.Length)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
                var type = png.Slice(offset + 4, 4);

                if (type.SequenceEqual("acTL"u8))
                    return true;

                // acTL must precede the first IDAT, so there is nothing to find past it.
                if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                    return false;

                // length + type + data + CRC, in arithmetic that cannot overflow: a chunk declaring
                // a length near int.MaxValue would otherwise wrap the offset negative and make the
                // next slice throw, turning a tiny malformed file into a 500. A length that runs
                // past the buffer means a malformed file either way, so stop and let the decoder
                // reject it with its own message.
                var nextOffset = (long)offset + 12 + length;
                if (nextOffset > png.Length)
                    return false;

                offset = (int)nextOffset;
            }

            return false;
        }
    }
}
