namespace backend.main.shared.providers.messages;

/// <summary>
/// Asks media-worker to validate one quarantined upload. Published by the API once it has claimed
/// the asset; carries everything the pipeline needs, so the worker never reads the database.
/// </summary>
public sealed class MediaValidationRequestMessage
{
    /// <summary>The asset's public id, echoed back on the result.</summary>
    public Guid MediaAssetId
    {
        get; init;
    }

    /// <summary>
    /// The claim this request was published under. Echoed back on the result, which is recorded
    /// only while the asset is still held by this claim.
    /// </summary>
    public int Attempt
    {
        get; init;
    }

    public string QuarantineBlobPath { get; init; } = string.Empty;

    /// <summary>Where the re-encoded image is published: the URL reserved when the upload was issued.</summary>
    public string PublicUrl { get; init; } = string.Empty;

    public string DeclaredContentType { get; init; } = string.Empty;

    /// <summary>Names the upload in the size message — "Event images" or "Club images".</summary>
    public string Subject { get; init; } = string.Empty;

    public DateTime RequestedAtUtc { get; init; } = DateTime.UtcNow;
}
