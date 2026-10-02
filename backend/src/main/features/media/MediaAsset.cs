namespace backend.main.features.media;

/// <summary>
/// One uploaded image and where it is in validation: from a presigned upload into the private
/// quarantine container, through decoding and re-encoding, to a public URL or a rejection.
/// </summary>
/// <remarks>
/// A table of its own rather than columns on <c>EventImage</c>. Club images and avatars are plain
/// string columns with no row to extend; an asset exists before it is attached to anything, and
/// often never is, so its lifetime matches no owning entity; and <c>EventImage</c> already carries
/// the cover index and alt-text policy.
/// <para>
/// Nothing references this table by foreign key yet — see the <c>mediaassets</c> migration. It is
/// an upload ledger, not a reference count: deleting an event deletes its blobs but leaves these
/// rows, and one URL can be shared by several recurrence occurrences.
/// </para>
/// </remarks>
public class MediaAsset
{
    public int Id
    {
        get; set;
    }

    /// <summary>The identifier clients see. <see cref="Id"/> never leaves the server.</summary>
    public Guid PublicId
    {
        get; set;
    }

    /// <summary>
    /// Who uploaded it. Null for legacy rows, whose uploader was never recorded, and once the
    /// uploader deletes their account: an image they put in someone else's club outlives them.
    /// </summary>
    public int? OwnerUserId
    {
        get; set;
    }

    /// <summary>The club the upload was issued for; null for a club that did not exist yet.</summary>
    public int? ClubId
    {
        get; set;
    }

    public int? EventId
    {
        get; set;
    }

    public MediaAssetStatus Status
    {
        get; set;
    }

    public MediaAssetOrigin Origin
    {
        get; set;
    }

    /// <summary>
    /// The blob's name inside the quarantine container while it has bytes there; cleared once
    /// they are promoted or deleted.
    /// </summary>
    public string? QuarantineBlobPath
    {
        get; set;
    }

    /// <summary>
    /// The public URL the re-encoded image is (or will be) served from. Reserved when the upload
    /// is issued, so the client can refer to the image before it exists, but nothing is stored
    /// there until the asset is <see cref="MediaAssetStatus.Ready"/>.
    /// </summary>
    public string? PublicUrl
    {
        get; set;
    }

    /// <summary>The content type the client asked for. Informational only; bytes decide.</summary>
    public required string DeclaredContentType
    {
        get; set;
    }

    /// <summary>The content type of the stored public bytes, set when the asset goes Ready.</summary>
    public string? ContentType
    {
        get; set;
    }

    public int? Width
    {
        get; set;
    }

    public int? Height
    {
        get; set;
    }

    /// <summary>Size of the stored public bytes.</summary>
    public long? ByteSize
    {
        get; set;
    }

    /// <summary>Why validation refused the upload, in words safe to show the uploader.</summary>
    public string? RejectionReason
    {
        get; set;
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the bytes passed validation. Null on legacy rows, which makes
    /// <c>ValidatedAt IS NULL</c> the query for images that were never checked.
    /// </summary>
    public DateTime? ValidatedAt
    {
        get; set;
    }

    /// <summary>How many times the validation pipeline has claimed this asset.</summary>
    public int AttemptCount
    {
        get; set;
    }

    /// <summary>
    /// Moves an in-memory asset to <paramref name="next"/>. Persisted transitions go through
    /// <see cref="IMediaAssetRepository.TryTransitionAsync"/>, which applies the same rule.
    /// </summary>
    /// <exception cref="InvalidOperationException">The move is illegal.</exception>
    public void TransitionTo(MediaAssetStatus next, DateTime now)
    {
        MediaAssetTransitions.EnsureAllowed(Status, next);
        Status = next;
        UpdatedAt = now;
    }
}
