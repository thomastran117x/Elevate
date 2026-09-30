namespace backend.main.features.media.contracts.responses;

/// <summary>Where an upload is in validation, for the editor that is waiting on it.</summary>
public sealed class MediaAssetResponse
{
    /// <summary>The asset's public id, as returned with the presigned upload.</summary>
    public Guid Id
    {
        get; init;
    }

    /// <summary>
    /// Serialised as a number and decoded by position on the client, so its members are only
    /// ever appended.
    /// </summary>
    public MediaAssetStatus Status
    {
        get; init;
    }

    /// <summary>The published image. Set only once the asset is Ready; nothing is there before.</summary>
    public string? Url
    {
        get; init;
    }

    /// <summary>Why the upload was refused, when it was.</summary>
    public string? RejectionReason
    {
        get; init;
    }
}
