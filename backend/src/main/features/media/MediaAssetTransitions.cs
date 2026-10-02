namespace backend.main.features.media;

/// <summary>
/// The legal moves between <see cref="MediaAssetStatus"/> values. Every status change goes
/// through <see cref="EnsureAllowed"/> before it is written, so an illegal one fails loudly in
/// code instead of leaving a row in a state nothing knows how to leave.
/// </summary>
public static class MediaAssetTransitions
{
    private static readonly IReadOnlyDictionary<MediaAssetStatus, MediaAssetStatus[]> Allowed =
        new Dictionary<MediaAssetStatus, MediaAssetStatus[]>
        {
            // Rejected here is the reaper expiring an upload nobody attached within a day.
            [MediaAssetStatus.PendingUpload] = [MediaAssetStatus.Uploaded, MediaAssetStatus.Rejected],
            // NeedsReview here is media-worker's reconciler giving up on an asset it has re-driven
            // too often; a released claim is back in Uploaded when it does.
            [MediaAssetStatus.Uploaded] =
            [
                MediaAssetStatus.Processing,
                MediaAssetStatus.Rejected,
                MediaAssetStatus.NeedsReview
            ],

            // Back to Uploaded releases a claim: a retryable fault (no processing slot, a storage
            // blip) or a claim so old its holder is presumed dead. The bytes are untouched either
            // way, so the next attempt starts from the same quarantine blob.
            [MediaAssetStatus.Processing] =
            [
                MediaAssetStatus.Ready,
                MediaAssetStatus.Rejected,
                MediaAssetStatus.NeedsReview,
                MediaAssetStatus.Uploaded
            ],
            [MediaAssetStatus.NeedsReview] = [MediaAssetStatus.Ready, MediaAssetStatus.Rejected],

            // Terminal. A Ready asset is served publicly and a Rejected one has had its bytes
            // deleted, so neither has anything left to re-run.
            [MediaAssetStatus.Ready] = [],
            [MediaAssetStatus.Rejected] = []
        };

    public static bool IsAllowed(MediaAssetStatus from, MediaAssetStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <exception cref="InvalidOperationException">The move is not in the table.</exception>
    public static void EnsureAllowed(MediaAssetStatus from, MediaAssetStatus to)
    {
        if (!IsAllowed(from, to))
            throw new InvalidOperationException($"A media asset cannot move from {from} to {to}.");
    }

    /// <summary>Whether nothing can move an asset out of <paramref name="status"/>.</summary>
    public static bool IsTerminal(MediaAssetStatus status) =>
        Allowed.TryGetValue(status, out var targets) && targets.Length == 0;
}
