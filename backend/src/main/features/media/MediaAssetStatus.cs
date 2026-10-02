namespace backend.main.features.media;

/// <summary>
/// Where an uploaded image is in its trip from the private quarantine container to a public URL.
/// </summary>
/// <remarks>
/// Stored as its name, so members may be reordered in source without a migration, but the API
/// serialises it as a number and the frontend decodes that by position: <b>only ever append</b>.
/// <see cref="MediaAssetTransitions"/> holds which moves between these are legal.
/// </remarks>
public enum MediaAssetStatus
{
    /// <summary>A presigned URL was issued; the server has not seen any bytes yet.</summary>
    PendingUpload,

    /// <summary>The bytes are in quarantine and waiting to be validated.</summary>
    Uploaded,

    /// <summary>The validation pipeline has claimed the asset and is working on it.</summary>
    Processing,

    /// <summary>Re-encoded bytes are at <see cref="MediaAsset.PublicUrl"/>. Terminal.</summary>
    Ready,

    /// <summary>The bytes failed validation and were deleted. Terminal.</summary>
    Rejected,

    /// <summary>
    /// Held for a person to decide. Set when media-worker's reconciler gives up on an asset it
    /// could not get validated, and reserved for image moderation. Its quarantined bytes are kept.
    /// </summary>
    NeedsReview
}

/// <summary>How a <see cref="MediaAsset"/> came to exist.</summary>
public enum MediaAssetOrigin
{
    /// <summary>Issued a presigned quarantine upload by this service.</summary>
    Upload,

    /// <summary>
    /// Backfilled from an image URL that was already live before quarantine existed. Never
    /// validated: <see cref="MediaAsset.ValidatedAt"/> stays null until something re-checks it.
    /// </summary>
    Legacy
}
