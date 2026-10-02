using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Re-drives assets that media-worker was asked to validate and never reported on: the request
/// was lost before reaching the broker, the worker died mid-pipeline, or the request was
/// dead-lettered. The <c>MediaAssets</c> row is the durable record of that work, which is why the
/// request is published directly rather than through an outbox.
/// </summary>
/// <remarks>
/// Backs off by attempt — two minutes after the first claim, then four, eight, and so on — and
/// after <see cref="MaxAttempts"/> claims gives up and parks the asset in NeedsReview, keeping
/// its bytes, rather than re-drive a poison image forever.
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
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);
    internal const int MaxAttempts = 5;
    internal const int BatchSize = 100;

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

    public async Task<MediaValidationReconcileResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await _repository.GetStalledBeforeAsync(now - BaseBackoff, BatchSize, cancellationToken);

        var redriven = 0;
        var parked = 0;

        foreach (var asset in candidates)
        {
            if (asset.UpdatedAt > now - BackoffFor(asset.AttemptCount))
                continue;

            switch (await ReconcileAsync(asset, cancellationToken))
            {
                case Reconciled.Redriven:
                    redriven++;
                    break;
                case Reconciled.Parked:
                    parked++;
                    break;
            }
        }

        return new MediaValidationReconcileResult(redriven, parked);
    }

    /// <summary>How long a claim may go unanswered before it is re-driven.</summary>
    internal static TimeSpan BackoffFor(int attemptCount)
    {
        var doublings = Math.Clamp(attemptCount - 1, 0, 10);
        var backoff = BaseBackoff * Math.Pow(2, doublings);
        return backoff < MaxBackoff ? backoff : MaxBackoff;
    }

    private async Task<Reconciled> ReconcileAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var givingUp = asset.AttemptCount >= MaxAttempts;

        if (asset.Status == MediaAssetStatus.Processing)
        {
            // Only the claim that went quiet: a result recorded since, or a newer claim, wins.
            var moved = await _repository.TryTransitionAsync(
                asset.Id,
                MediaAssetStatus.Processing,
                givingUp ? MediaAssetStatus.NeedsReview : MediaAssetStatus.Uploaded,
                cancellationToken: cancellationToken,
                whenAttempt: asset.AttemptCount);

            if (!moved)
                return Reconciled.Nothing;

            if (givingUp)
                return Parked(asset);
        }
        else if (givingUp)
        {
            return await _repository.TryTransitionAsync(
                    asset.Id,
                    MediaAssetStatus.Uploaded,
                    MediaAssetStatus.NeedsReview,
                    cancellationToken: cancellationToken,
                    whenAttempt: asset.AttemptCount)
                ? Parked(asset)
                : Reconciled.Nothing;
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
            return Reconciled.Nothing;

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
            return Reconciled.Nothing;
        }

        Logger.Info($"[MediaValidationReconciler] Re-drove media asset {asset.PublicId} under claim {attempt}.");
        return Reconciled.Redriven;
    }

    private static Reconciled Parked(MediaAsset asset)
    {
        Logger.Warn(
            $"[MediaValidationReconciler] Gave up on media asset {asset.PublicId} after {asset.AttemptCount} claims; it needs review.");
        return Reconciled.Parked;
    }

    private enum Reconciled
    {
        Nothing,
        Redriven,
        Parked
    }
}

public readonly record struct MediaValidationReconcileResult(int Redriven, int Parked);
