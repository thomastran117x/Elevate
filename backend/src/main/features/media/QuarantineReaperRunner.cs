using Azure;

using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Keeps the quarantine container from growing without bound. Releases claims whose attach died
/// mid-pipeline, expires uploads that were issued and never attached, and deletes quarantined
/// blobs a day old that no live asset is waiting on.
/// </summary>
/// <remarks>
/// <c>OrphanBlobCleanupRunner</c> cannot do this: it lists only the public container, under its
/// configured prefixes, so it never sees quarantine at all. And nothing else removes a blob a
/// client uploaded and then never attached.
/// <para>
/// Not leader-elected. Every step is a conditional update or an idempotent delete, so two
/// instances sweeping at once only duplicate work.
/// </para>
/// </remarks>
public sealed class QuarantineReaperRunner
{
    /// <summary>
    /// Age past which a quarantined upload is abandoned. The upload intent that would let anyone
    /// attach it expires after twenty minutes, so a day leaves a wide margin.
    /// </summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Upper bound on assets expired, and separately on blobs deleted, per run.</summary>
    internal const int BatchSize = 200;

    internal const string ExpiredMessage = "Upload expired before it was attached.";

    // Process-wide so a missing container is reported once, not every hour.
    private static int _reportedMissingContainer;

    private readonly IMediaAssetRepository _repository;
    private readonly IAzureBlobService _blobService;
    private readonly TimeProvider _timeProvider;

    public QuarantineReaperRunner(
        IMediaAssetRepository repository,
        IAzureBlobService blobService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _blobService = blobService;
        _timeProvider = timeProvider;
    }

    public async Task<QuarantineReapResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var cutoff = now - MaxAge;

        var released = await ReleaseStaleClaimsAsync(
            (now - MediaAssetService.StaleProcessingAfter).UtcDateTime, cancellationToken);
        var expired = await ExpireUnattachedAsync(cutoff.UtcDateTime, cancellationToken);

        int deleted;
        try
        {
            deleted = await DeleteAbandonedBlobsAsync(cutoff, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == "ContainerNotFound")
        {
            // Quarantine has not been provisioned here, most likely because storage.quarantine
            // was never switched on. There is nothing to reap, and nothing worth an hourly alarm.
            if (Interlocked.Exchange(ref _reportedMissingContainer, 1) == 0)
            {
                Logger.Warn(
                    "[QuarantineReaperRunner] The quarantine container does not exist; skipping blob cleanup. " +
                    "Run the storage-provision task if quarantine is meant to be on.");
            }

            deleted = 0;
        }

        return new QuarantineReapResult(expired, deleted, released);
    }

    /// <summary>
    /// Hands back claims whose attach died before it could release them — the process was killed
    /// mid-pipeline, so no exception handler ran. Released to Uploaded rather than expired here:
    /// the user may still retry within the intent's lifetime, and an upload nobody retries is
    /// expired by the pass below once it is a day old, its bytes with it.
    /// </summary>
    /// <remarks>
    /// Uses the same threshold an attach does before taking over a claim, so the reaper never
    /// releases one a live request could still be working on.
    /// </remarks>
    private async Task<int> ReleaseStaleClaimsAsync(DateTime claimedBefore, CancellationToken cancellationToken)
    {
        var released = 0;
        var stale = await _repository.GetProcessingClaimedBeforeAsync(claimedBefore, BatchSize, cancellationToken);

        foreach (var asset in stale)
        {
            if (await _repository.TryTransitionAsync(
                    asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Uploaded, cancellationToken: cancellationToken))
            {
                released++;
            }
        }

        return released;
    }

    /// <summary>
    /// Moves assets that are still waiting for an attach nobody can make any more to Rejected,
    /// deleting their quarantined bytes if the client ever uploaded any.
    /// </summary>
    private async Task<int> ExpireUnattachedAsync(DateTime cutoff, CancellationToken cancellationToken)
    {
        var expired = 0;
        var stale = await _repository.GetUnattachedIssuedBeforeAsync(cutoff, BatchSize, cancellationToken);

        foreach (var asset in stale)
        {
            var moved = await _repository.TryTransitionAsync(
                asset.Id,
                asset.Status,
                MediaAssetStatus.Rejected,
                new MediaAssetChanges { RejectionReason = ExpiredMessage, ClearQuarantineBlobPath = true },
                cancellationToken);

            // Lost the race to a late attach, which now owns the asset's fate.
            if (!moved)
                continue;

            expired++;
            if (!string.IsNullOrWhiteSpace(asset.QuarantineBlobPath))
                await _blobService.DeleteQuarantineBlobAsync(asset.QuarantineBlobPath);
        }

        return expired;
    }

    /// <summary>
    /// Deletes quarantined blobs older than the cutoff unless an asset is working on them. Catches
    /// what the expiry pass cannot: blobs whose asset row is gone (the uploader deleted their
    /// account) or whose delete failed after the asset reached a terminal state.
    /// </summary>
    private async Task<int> DeleteAbandonedBlobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = 0;

        await foreach (var blob in _blobService.ListQuarantineBlobsAsync(cancellationToken))
        {
            if (deleted >= BatchSize)
                break;

            if (blob.LastModified is null || blob.LastModified > cutoff)
                continue;

            var owner = await _repository.GetByQuarantineBlobPathAsync(blob.Path, cancellationToken);

            // Processing means an attach is decoding these bytes right now — a stale claim was
            // released above, so one still Processing here is fresh. Pending and Uploaded owners
            // belong to the expiry pass, which may simply have hit its batch limit; it deletes
            // their bytes when it expires them, and not before.
            if (owner is { Status: MediaAssetStatus.Processing or MediaAssetStatus.PendingUpload or MediaAssetStatus.Uploaded })
                continue;

            await _blobService.DeleteQuarantineBlobAsync(blob.Path);
            deleted++;
        }

        return deleted;
    }
}

public readonly record struct QuarantineReapResult(int ExpiredAssets, int DeletedBlobs, int ReleasedClaims = 0);
