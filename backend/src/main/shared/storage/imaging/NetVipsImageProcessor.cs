using System.Buffers.Binary;

using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

using NetVips;

using VipsImage = NetVips.Image;

namespace backend.main.shared.storage.imaging
{
    /// <summary>
    /// <see cref="IImageProcessor"/> on libvips, through NetVips. Decoding is native, so the
    /// header is checked before any pixel buffer exists and only the four loaders
    /// <see cref="ImageSignatureInspector"/> admits are ever called.
    /// </summary>
    public sealed class NetVipsImageProcessor : IImageProcessor
    {
        internal const string UnsupportedFormatMessage = "Only JPEG, PNG, WEBP, and GIF images are supported.";
        internal const string TooLargeMessage = "The image dimensions are too large.";
        internal const string AnimatedMessage = "Animated images are not supported. Upload a single-frame image.";
        internal const string UnreadableMessage = "The image could not be read.";
        internal const string BusyMessage = "Image processing is busy. Try again shortly.";

        private readonly ImageProcessingOptions _options;
        private readonly SemaphoreSlim _slots;
        private readonly TimeSpan _slotWaitTimeout;

        static NetVipsImageProcessor()
        {
            if (!ModuleInitializer.VipsInitialized)
            {
                throw new InvalidOperationException(
                    "libvips could not be loaded; the NetVips.Native binaries are missing for this platform.",
                    ModuleInitializer.Exception);
            }

            // libvips caches operations by their arguments so a repeated call is free. Every call
            // here is on a different upload, so the cache would only hold decoded pixels from
            // untrusted files in memory after their request had finished.
            Cache.Max = 0;
            Cache.MaxMem = 0;
            Cache.MaxFiles = 0;

            // The loaders are chosen explicitly below, but this also takes the operations libvips
            // itself marks as unsafe for untrusted input out of reach of anything that sniffs.
            NetVips.NetVips.BlockUntrusted = true;
        }

        public NetVipsImageProcessor(IOptions<ImageProcessingOptions> options)
        {
            _options = options.Value;
            _slots = new SemaphoreSlim(_options.MaxConcurrentOperations, _options.MaxConcurrentOperations);
            _slotWaitTimeout = TimeSpan.FromSeconds(_options.SlotWaitTimeoutSeconds);
        }

        public async Task<ProcessedImage> ProcessAsync(
            Stream source,
            ImageProcessingProfile profile,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            var maxEdge = MaxEdgeFor(profile);

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
                // Buffer once: libvips loads from a byte array, and every later step reads it.
                var buffered = await BufferAsync(source, cancellationToken);

                // The cheap byte check first: it keeps anything that is not one of the four formats
                // away from every decoder, and it is the same gate the presigned path applies.
                if (!ImageSignatureInspector.TryDetect(buffered, out var signature))
                    throw new BadRequestException(UnsupportedFormatMessage);

                // libvips' PNG loader reads only the default image of an APNG, so left to the
                // decoder an animation would be silently flattened to its first frame. The
                // animation control chunk is what makes a PNG an APNG, so finding it here refuses
                // it with the same message as an animated GIF or WebP.
                if (signature.Format == ImageFormat.Png && HasApngAnimationChunk(buffered))
                    throw new BadRequestException(AnimatedMessage);

                // Only the decode is forgiven. Encoding runs on pixels that already decoded, so a
                // failure there is ours, not the uploader's, and belongs in the 500s where alerting
                // can see it.
                using var image = Decode(buffered, signature.Format, maxEdge, cancellationToken);
                return Encode(image);
            }
            finally
            {
                _slots.Release();
            }
        }

        /// <summary>
        /// Validates the header, then decodes, shrinks and orients the image into memory.
        /// </summary>
        /// <remarks>
        /// libvips evaluates lazily: loading reads only the header, and pixels are decoded when
        /// something downstream asks for them. <see cref="VipsImage.CopyMemory"/> is that request,
        /// so every decoder error surfaces inside this method, where it can be told apart from a
        /// fault of ours, instead of later in the encoder.
        /// </remarks>
        private VipsImage Decode(
            byte[] source,
            ImageFormat format,
            int maxEdge,
            CancellationToken cancellationToken)
        {
            try
            {
                // 1. Header only, so a 100 KB file declaring 100000x100000 is refused here, before
                // it can ask for 40 GB.
                using var loaded = Load(source, format);

                if (loaded.Width > _options.MaxDimension ||
                    loaded.Height > _options.MaxDimension ||
                    (long)loaded.Width * loaded.Height > _options.MaxPixels)
                {
                    throw new BadRequestException(TooLargeMessage);
                }

                // An animated GIF or WebP is a frame-count bomb, and flattening it to its first
                // frame silently destroys what the user meant to upload. Refusing says so. The
                // frontend's image-file-validation.ts mirrors this rule (and the APNG one above)
                // to warn when the file is picked; a change to either must change both.
                if (loaded.Contains("n-pages") && (int)loaded.Get("n-pages") > 1)
                    throw new BadRequestException(AnimatedMessage);

                // Lets a cancelled request stop the decode part-way: one at the pixel limit takes
                // long enough that checking only between stages would let a disconnected request
                // hold its buffers and its slot to the end.
                loaded.SetProgress(NoProgress.Instance, cancellationToken);

                // 2. Shrink, then orient. The thumbnail operation resizes first and applies the
                // EXIF orientation to the small result, so a full-resolution rotation never needs
                // a second full-size buffer, and it never upscales. EXIF is dropped with the rest
                // of the metadata at encode time, so skipping the rotation would store every
                // portrait phone photo on its side. Converting to sRGB through any embedded ICC
                // profile keeps a wide-gamut or CMYK photo's colours once the profile is gone.
                using var thumbnail = loaded.ThumbnailImage(
                    maxEdge,
                    height: maxEdge,
                    size: Enums.Size.Down,
                    outputProfile: "srgb",
                    failOn: Enums.FailOn.Error);
                using var srgb = ToEightBitSrgb(thumbnail);

                // 3. Run the whole pipeline now (see remarks).
                var decoded = srgb.CopyMemory();
                cancellationToken.ThrowIfCancellationRequested();
                return decoded;
            }
            catch (Exception ex) when (IsDecoderFailure(ex))
            {
                // A killed evaluation surfaces as a VipsException; it was the caller leaving, not a
                // bad file.
                cancellationToken.ThrowIfCancellationRequested();

                // Deliberately broad. libvips reports every decoder failure as a VipsException,
                // but its .NET binding can raise others for a malformed header, and listing types
                // has missed cases before, so the filter names what is *not* the uploader's fault
                // instead.
                //
                // Logged without the stack: any client can produce these at will, so they are
                // ordinary 400 validation rather than something to fill the log with.
                Logger.Info($"[NetVipsImageProcessor] Rejected an undecodable image ({ex.GetType().Name}).");
                throw new BadRequestException(UnreadableMessage);
            }
        }

        /// <summary>
        /// Opens the buffer with the loader for the format its signature claims, never by sniffing,
        /// so no other libvips parser sees the bytes. Sequential access lets libvips stream the
        /// decode through the shrink rather than holding the image to read it in any order, and
        /// only the first frame is ever loaded.
        /// </summary>
        private static VipsImage Load(byte[] source, ImageFormat format) => format switch
        {
            ImageFormat.Jpeg => VipsImage.JpegloadBuffer(source, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error),
            ImageFormat.Png => VipsImage.PngloadBuffer(source, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error),
            ImageFormat.Webp => VipsImage.WebploadBuffer(source, n: 1, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error),
            ImageFormat.Gif => VipsImage.GifloadBuffer(source, n: 1, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error),
            _ => throw new BadRequestException(UnsupportedFormatMessage)
        };

        /// <summary>
        /// The WebP encoder wants 8-bit sRGB. Greyscale and 16-bit inputs are converted, which
        /// scales rather than clips; the common case passes through as a new reference.
        /// </summary>
        private static VipsImage ToEightBitSrgb(VipsImage image) =>
            image.Interpretation == Enums.Interpretation.Srgb && image.Format == Enums.BandFormat.Uchar
                ? image.Copy()
                : image.Colourspace(Enums.Interpretation.Srgb);

        private int MaxEdgeFor(ImageProcessingProfile profile) => profile switch
        {
            ImageProcessingProfile.Avatar => _options.AvatarMaxEdge,
            ImageProcessingProfile.Gallery => _options.GalleryMaxEdge,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown image processing profile.")
        };

        /// <summary>
        /// 4. Always WebP: one encoder path, a format the uploader did not choose, and keeping no
        /// metadata means only pixels are written. This is the single place metadata is dropped:
        /// EXIF (GPS included), IPTC, XMP and ICC are all left out of the encoded output.
        /// </summary>
        private ProcessedImage Encode(VipsImage image)
        {
            var content = image.WebpsaveBuffer(q: _options.WebpQuality, keep: Enums.ForeignKeep.None);
            return new ProcessedImage(content, image.Width, image.Height);
        }

        /// <summary>
        /// Whether an exception raised while decoding says the uploaded file is wrong, as opposed
        /// to something being wrong here.
        /// </summary>
        /// <remarks>
        /// Cancellation and the pipeline's own rejections are not decoder failures. Neither are
        /// resource and lifetime faults: reporting an exhausted heap or a disposed object as "your
        /// image is unreadable" tells the user their good photo is broken and keeps a real server
        /// fault out of the 500-level alerting.
        /// </remarks>
        private static bool IsDecoderFailure(Exception exception) => exception switch
        {
            AppException or OperationCanceledException or OutOfMemoryException or ObjectDisposedException => false,
            IOException => false,
            _ => true
        };

        /// <summary>
        /// Copies the upload into a single array that every later step reads. A stream that can
        /// seek is read from its start, so the whole upload is buffered however the caller left
        /// the position.
        /// </summary>
        private static async Task<byte[]> BufferAsync(Stream source, CancellationToken cancellationToken)
        {
            var capacity = source.CanSeek ? checked((int)source.Length) : 0;
            using var buffer = new MemoryStream(capacity);

            if (source.CanSeek)
                source.Position = 0;

            await source.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
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

        /// <summary>
        /// <see cref="VipsImage.SetProgress(IProgress{int}, CancellationToken)"/> needs a progress
        /// sink to deliver cancellation; nothing here reports progress.
        /// </summary>
        private sealed class NoProgress : IProgress<int>
        {
            public static readonly NoProgress Instance = new();

            public void Report(int value)
            {
            }
        }
    }
}
