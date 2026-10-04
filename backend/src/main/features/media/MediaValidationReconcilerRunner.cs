using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Re-drives assets that media-worker was asked to validate and never reported on: the request
/// was lost before reaching the broker, the worker died mid-pipeline, or the request was
/// dead-lettered. The <c>MediaAssets</c> row is the durable record of that work, which is why the
/// request is published directly rather than through an outbox.
/// </summary>
/// <remarks>
/// Re-drives only while the upload can still be attached — within its intent's lifetime — because
/// after that no request can use the verdict. Backs off by attempt in that window: two minutes
/// after a claim, then four, then eight. A claim still open once the window has passed is released
/// back to Uploaded, where the quarantine reaper expires it with its bytes once it is a day old.
/// Nothing here gives up into NeedsReview: an unanswered claim says nothing about the image, so an
/// outage must not park good uploads where nothing would ever release them.
/// <para>
/// Not leader-elected. Every step is a conditional update keyed to the claim, so two instances
/// sweeping at once only race for the same claim, and one of them wins.
/// </para>
/// <para>
/// Uploads still PendingUpload are not touched: only an attach moves an asset past that, and an
/// upload nobody attaches is <see cref="QuarantineReaperRunner"/>'s to expire, not worth decoding.
/// </para>
/// </remarks>
public sealed class MediaValidationReconcilerRunner
{
    internal static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(8);

    /// <summary>Upper bound on assets re-driven or released per run.</summary>
    internal const int BatchSize = 100;

    /// <summary>
    /// Upper bound on pages read per run. Rows still inside their backoff are skipped rather than
    /// counted, so without paging a batch full of them would hide every due row behind it.
    /// </summary>
    internal const int MaxPages = 20;

    /// <summary>
    /// The attach that knew whether this was an event or a club image is long gone, so a size
    /// rejection on a re-drive names the upload generically.
    /// </summary>
    internal const string RedriveSubject = "Images";

    private readonly IMediaAssetRepository _repository;
    private readonly IMediaValidationDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;

    public MediaValidationReconcilerRunner(
        IMediaAssetRepository repository,
        IMediaValidationDispatcher dispatcher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
    }

    /// <summary>How long after it was issued an upload can still be attached.</summary>
    internal static TimeSpan AttachWindow => BlobUploadIntentValidator.IntentTtl;

    public async Task<MediaValidationReconcileResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var attachableSince = now - AttachWindow;

        var redriven = 0;
        var released = 0;
        (DateTime UpdatedAt, int Id)? after = null;

        for (var page = 0; page < MaxPages && redriven + released < BatchSize; page++)
        {
            var rows = await _repository.GetStalledBeforeAsync(
                now - BaseBackoff, attachableSince, BatchSize, after, cancellationToken);

            foreach (var asset in rows)
            {
                if (redriven + released >= BatchSize)
                    break;

                if (asset.CreatedAt < attachableSince)
                {
                    // Only Processing rows come back past the window.
                    if (await ReleaseAsync(asset, cancellationToken))
                        released++;
                    continue;
                }

                if (asset.UpdatedAt > now - BackoffFor(asset.AttemptCount))
                    continue;

                if (await RedriveAsync(asset, cancellationToken))
                    redriven++;
            }

            if (rows.Count < BatchSize)
                break;

            after = (rows[^1].UpdatedAt, rows[^1].Id);
        }

        return new MediaValidationReconcileResult(redriven, released);
    }

    /// <summary>How long a claim may go unanswered before it is re-driven.</summary>
    internal static TimeSpan BackoffFor(int attemptCount)
    {
        var doublings = Math.Clamp(attemptCount - 1, 0, 10);
        var backoff = BaseBackoff * Math.Pow(2, doublings);
        return backoff < MaxBackoff ? backoff : MaxBackoff;
    }

    /// <summary>
    /// Hands an expired claim back without re-driving it. Its verdict, should it still arrive, is
    /// recorded against Uploaded; otherwise the reaper expires the asset.
    /// </summary>
    private async Task<bool> ReleaseAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var moved = await _repository.TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Processing,
            MediaAssetStatus.Uploaded,
            cancellationToken: cancellationToken,
            whenAttempt: asset.AttemptCount);

        if (moved)
        {
            Logger.Info(
                $"[MediaValidationReconciler] Released claim {asset.AttemptCount} on media asset {asset.PublicId}; it can no longer be attached.");
        }

        return moved;
    }

    private async Task<bool> RedriveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        // Only the claim that went quiet: a result recorded since, or a newer claim, wins.
        if (asset.Status == MediaAssetStatus.Processing
            && !await _repository.TryTransitionAsync(
                asset.Id,
                MediaAssetStatus.Processing,
                MediaAssetStatus.Uploaded,
                cancellationToken: cancellationToken,
                whenAttempt: asset.AttemptCount))
        {
            return false;
        }

        var claimed = await _repository.TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Uploaded,
            MediaAssetStatus.Processing,
            new MediaAssetChanges { CountAttempt = true },
            cancellationToken,
            whenAttempt: asset.AttemptCount);

        // An attach got there first and is driving it now.
        if (!claimed)
            return false;

        var attempt = asset.AttemptCount + 1;
        try
        {
            await _dispatcher.DispatchAsync(asset, attempt, RedriveSubject, cancellationToken);
        }
        catch (Exception ex)
        {
            // Hand the claim back so the next sweep, or the user's own retry, can pick it up
            // without waiting for it to go stale. CancellationToken.None: this must happen
            // even when shutdown is what interrupted the dispatch.
            Logger.Warn(ex, $"[MediaValidationReconciler] Could not re-drive media asset {asset.PublicId}.");
            await _repository.TryTransitionAsync(
                asset.Id,
                MediaAssetStatus.Processing,
                MediaAssetStatus.Uploaded,
                cancellationToken: CancellationToken.None,
                whenAttempt: attempt);
            return false;
        }

        Logger.Info($"[MediaValidationReconciler] Re-drove media asset {asset.PublicId} under claim {attempt}.");
        return true;
    }
}

public readonly record struct MediaValidationReconcileResult(int Redriven, int Released);
