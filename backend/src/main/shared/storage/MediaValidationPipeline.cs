using backend.main.shared.exceptions.http;
using backend.main.shared.storage.imaging;

namespace backend.main.shared.storage;

/// <summary>
/// Turns a quarantined upload into a published image, or into a reason it was refused: inspect
/// the bytes, decode and re-encode them, and write the result to the public URL that was
/// reserved when the upload was issued.
/// </summary>
/// <remarks>
/// Deliberately knows nothing about the database or about what the image is attached to. It
/// runs synchronously inside the attach request for now; moving it into a dedicated worker is
/// meant to be a relocation of this class, not a rewrite of it.
/// <para>
/// It never deletes the quarantine copy. The caller does that once it has recorded the outcome,
/// so a crash between promotion and recording leaves intact bytes for the retry to start from —
/// and the retry overwrites the public blob with the same result.
/// </para>
/// </remarks>
public sealed class MediaValidationPipeline
{
    private readonly IAzureBlobService _blobService;
    private readonly IImageProcessor _imageProcessor;

    public MediaValidationPipeline(IAzureBlobService blobService, IImageProcessor imageProcessor)
    {
        _blobService = blobService;
        _imageProcessor = imageProcessor;
    }

    /// <summary>
    /// Validates the quarantined blob and, if it passes, publishes the re-encoded image.
    /// </summary>
    /// <param name="subject">Names the upload in the size message — "Event images" or "Club images".</param>
    /// <exception cref="NotAvailableException">No processing slot came free in time. Retryable.</exception>
    /// <remarks>
    /// Anything other than a verdict about the uploaded bytes — a storage fault, cancellation, a
    /// bug — propagates rather than becoming a rejection, so the caller can release the asset for
    /// another attempt instead of blaming the uploader for our failure.
    /// </remarks>
    public async Task<MediaValidationOutcome> RunAsync(
        string quarantineBlobPath,
        string publicUrl,
        string? declaredContentType,
        string subject,
        CancellationToken cancellationToken = default)
    {
        var inspection = await _blobService.InspectQuarantineBlobAsync(
            quarantineBlobPath, cancellationToken: cancellationToken);
        if (inspection == null)
            return MediaValidationOutcome.Reject(ImageUploadGate.DidNotCompleteMessage);

        var rejection = ImageUploadGate.Evaluate(
            inspection.Value, declaredContentType, _blobService.MaxImageBytes, subject, out _);
        if (rejection != null)
            return MediaValidationOutcome.Reject(rejection);

        await using var source = await _blobService.OpenQuarantineBlobReadAsync(quarantineBlobPath, cancellationToken);
        if (source == null)
            return MediaValidationOutcome.Reject(ImageUploadGate.DidNotCompleteMessage);

        // The inspection read the stored length, but the download is read against the cap again
        // rather than trusting that number: nothing above the cap is ever buffered.
        using var buffered = await ReadBoundedAsync(source, _blobService.MaxImageBytes, cancellationToken);
        if (buffered == null)
            return MediaValidationOutcome.Reject(ImageUploadGate.TooLargeMessage(subject, _blobService.MaxImageBytes));

        ProcessedImage processed;
        try
        {
            processed = await _imageProcessor.ProcessAsync(buffered, ImageProcessingProfile.Gallery, cancellationToken);
        }
        catch (BadRequestException ex)
        {
            // The processor's 400s are all about the file: undecodable, animated, too many
            // pixels. Those are the uploader's to fix, so they become the asset's reason.
            return MediaValidationOutcome.Reject(ex.Message);
        }

        await _blobService.UploadProcessedImageToAsync(processed, publicUrl, cancellationToken);

        return MediaValidationOutcome.Accept(
            processed.ContentType,
            processed.Width,
            processed.Height,
            processed.Content.LongLength);
    }

    /// <summary>
    /// Buffers at most <paramref name="maxBytes"/> of <paramref name="source"/>; returns null if
    /// there is more than that.
    /// </summary>
    private static async Task<MemoryStream?> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffered = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;

            if (buffered.Length + read > maxBytes)
            {
                await buffered.DisposeAsync();
                return null;
            }

            buffered.Write(chunk, 0, read);
        }

        buffered.Position = 0;
        return buffered;
    }
}

/// <summary>What the pipeline decided about one upload.</summary>
public sealed record MediaValidationOutcome
{
    public bool Accepted
    {
        get; private init;
    }

    /// <summary>Why the upload was refused, in words safe to show the uploader.</summary>
    public string? RejectionReason
    {
        get; private init;
    }

    public string? ContentType
    {
        get; private init;
    }

    public int Width
    {
        get; private init;
    }

    public int Height
    {
        get; private init;
    }

    public long ByteSize
    {
        get; private init;
    }

    public static MediaValidationOutcome Accept(string contentType, int width, int height, long byteSize) =>
        new()
        {
            Accepted = true,
            ContentType = contentType,
            Width = width,
            Height = height,
            ByteSize = byteSize
        };

    public static MediaValidationOutcome Reject(string reason) =>
        new()
        {
            Accepted = false,
            RejectionReason = reason
        };
}
