using backend.main.shared.storage;

namespace backend.main.shared.providers.messages;

/// <summary>
/// What media-worker decided about one upload. The API's status consumer records it on the asset,
/// which is the only database write the validation produces.
/// </summary>
public sealed class MediaValidationResultMessage
{
    public Guid MediaAssetId
    {
        get; init;
    }

    /// <summary>The claim the request was published under.</summary>
    public int Attempt
    {
        get; init;
    }

    public bool Accepted
    {
        get; init;
    }

    /// <summary>Why the upload was refused, in words safe to show the uploader.</summary>
    public string? RejectionReason
    {
        get; init;
    }

    public string? ContentType
    {
        get; init;
    }

    public int Width
    {
        get; init;
    }

    public int Height
    {
        get; init;
    }

    public long ByteSize
    {
        get; init;
    }

    public DateTime ProcessedAtUtc { get; init; } = DateTime.UtcNow;

    public static MediaValidationResultMessage From(
        MediaValidationRequestMessage request,
        MediaValidationOutcome outcome) => new()
        {
            MediaAssetId = request.MediaAssetId,
            Attempt = request.Attempt,
            Accepted = outcome.Accepted,
            RejectionReason = outcome.RejectionReason,
            ContentType = outcome.ContentType,
            Width = outcome.Width,
            Height = outcome.Height,
            ByteSize = outcome.ByteSize
        };

    public MediaValidationOutcome ToOutcome() =>
        Accepted
            ? MediaValidationOutcome.Accept(ContentType ?? string.Empty, Width, Height, ByteSize)
            : MediaValidationOutcome.Reject(RejectionReason ?? string.Empty);
}
