using backend.main.shared.attributes.repository;

namespace backend.main.features.media;

public interface IMediaAssetRepository
{
    /// <summary>Stages a new asset. The caller commits it.</summary>
    Task AddAsync(MediaAsset asset);

    Task<MediaAsset?> GetByPublicIdAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>The assets among <paramref name="publicIds"/> that exist, by public id, in one read.</summary>
    Task<Dictionary<Guid, MediaAsset>> GetByPublicIdsAsync(
        IReadOnlyCollection<Guid> publicIds,
        CancellationToken cancellationToken = default);

    Task<MediaAsset?> GetByQuarantineBlobPathAsync(
        string quarantineBlobPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the asset from <paramref name="from"/> to <paramref name="to"/> in one conditional
    /// statement, applying <paramref name="changes"/> with it. Returns false when the row was no
    /// longer in <paramref name="from"/>: someone else moved it first.
    /// </summary>
    /// <remarks>
    /// Executes immediately rather than staging tracked edits, so it never flushes a caller's
    /// half-built event or club, and so two requests racing to claim the same asset cannot both
    /// win.
    /// <para>
    /// <see cref="NoRetryAttribute"/> because a retry after a lost acknowledgement would find the
    /// row already moved, report false, and make the caller that actually won believe it lost.
    /// Callers reload and act on the current status instead.
    /// </para>
    /// </remarks>
    /// <param name="whenAttempt">
    /// Also require <see cref="MediaAsset.AttemptCount"/> to equal this. Each claim bumps the
    /// count, so it identifies the claim: a holder that passes its own attempt cannot finish or
    /// release a claim someone else has since taken over.
    /// </param>
    /// <exception cref="InvalidOperationException">The move is illegal.</exception>
    [NoRetry]
    Task<bool> TryTransitionAsync(
        int id,
        MediaAssetStatus from,
        MediaAssetStatus to,
        MediaAssetChanges? changes = null,
        CancellationToken cancellationToken = default,
        int? whenAttempt = null);

    /// <summary>
    /// Assets still waiting in <see cref="MediaAssetStatus.PendingUpload"/> or
    /// <see cref="MediaAssetStatus.Uploaded"/> that were issued before
    /// <paramref name="createdBefore"/>. Their upload intent expired long ago, so nothing can
    /// attach them any more: the client took a URL and never used it, or used it and never
    /// attached the result.
    /// </summary>
    Task<List<MediaAsset>> GetUnattachedIssuedBeforeAsync(
        DateTime createdBefore,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assets in <see cref="MediaAssetStatus.Processing"/> whose claim was last touched before
    /// <paramref name="updatedBefore"/>: their attach died without releasing it.
    /// </summary>
    Task<List<MediaAsset>> GetProcessingClaimedBeforeAsync(
        DateTime updatedBefore,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of attached assets that nothing has moved since <paramref name="updatedBefore"/>,
    /// in (<see cref="MediaAsset.UpdatedAt"/>, <see cref="MediaAsset.Id"/>) order: every
    /// <see cref="MediaAssetStatus.Processing"/> claim, and <see cref="MediaAssetStatus.Uploaded"/>
    /// assets issued at or after <paramref name="issuedSince"/>. Older Uploaded assets can no
    /// longer be attached, and are left for the reaper to expire.
    /// </summary>
    /// <param name="after">The last row of the previous page; null for the first.</param>
    Task<List<MediaAsset>> GetStalledBeforeAsync(
        DateTime updatedBefore,
        DateTime issuedSince,
        int limit,
        (DateTime UpdatedAt, int Id)? after = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Columns written alongside a status change. Null members are left as they are.
/// </summary>
public sealed record MediaAssetChanges
{
    public string? RejectionReason
    {
        get; init;
    }

    public string? ContentType
    {
        get; init;
    }

    public int? Width
    {
        get; init;
    }

    public int? Height
    {
        get; init;
    }

    public long? ByteSize
    {
        get; init;
    }

    public DateTime? ValidatedAt
    {
        get; init;
    }

    /// <summary>The quarantine bytes are gone, promoted or deleted.</summary>
    public bool ClearQuarantineBlobPath
    {
        get; init;
    }

    /// <summary>This transition is the pipeline claiming the asset for another attempt.</summary>
    public bool CountAttempt
    {
        get; init;
    }
}
