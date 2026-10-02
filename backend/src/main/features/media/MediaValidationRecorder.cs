using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Writes what <see cref="MediaValidationPipeline"/> decided onto the asset it was run for: the
/// one place an asset is promoted to Ready or Rejected, whether the pipeline ran inside the attach
/// request or in media-worker.
/// </summary>
/// <remarks>
/// Idempotent. Each move is conditional on the asset still being Processing under the claim the
/// outcome belongs to, so a redelivered result, or one from a claim that has since been taken
/// over, finds the row already moved and changes nothing.
/// </remarks>
public sealed class MediaValidationRecorder
{
    private readonly IMediaAssetRepository _repository;
    private readonly IAzureBlobService _blobService;
    private readonly TimeProvider _timeProvider;

    public MediaValidationRecorder(
        IMediaAssetRepository repository,
        IAzureBlobService blobService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _blobService = blobService;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Records <paramref name="outcome"/> for claim <paramref name="attempt"/> on
    /// <paramref name="asset"/>, then deletes the quarantined bytes.
    /// </summary>
    /// <returns>
    /// True once the outcome is recorded. False when the claim was no longer held — taken over,
    /// already recorded, or the row deleted with its account — in which case nothing is changed
    /// and the quarantined bytes are left for whoever holds the asset now. Whatever this attempt
    /// already published at the reserved URL is the same bytes the holder will publish, and if no
    /// holder ever does, nothing references the blob and the orphan sweeper reclaims it.
    /// </returns>
    public async Task<bool> RecordAsync(MediaAsset asset, int attempt, MediaValidationOutcome outcome)
    {
        // CancellationToken.None throughout: the pipeline has already done its work, and an
        // outcome that is dropped halfway leaves the asset to be re-run for nothing.
        var changes = outcome.Accepted
            ? new MediaAssetChanges
            {
                ContentType = outcome.ContentType,
                Width = outcome.Width,
                Height = outcome.Height,
                ByteSize = outcome.ByteSize,
                ValidatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                ClearQuarantineBlobPath = true
            }
            : new MediaAssetChanges
            {
                RejectionReason = outcome.RejectionReason ?? MediaAssetService.GenericRejectionMessage,
                ClearQuarantineBlobPath = true
            };

        var recorded = await _repository.TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Processing,
            outcome.Accepted ? MediaAssetStatus.Ready : MediaAssetStatus.Rejected,
            changes,
            CancellationToken.None,
            whenAttempt: attempt);

        if (!recorded)
        {
            Logger.Warn(
                $"[MediaValidationRecorder] Claim {attempt} on media asset {asset.PublicId} was no longer held when its outcome arrived.");
            return false;
        }

        // Only now: had the status write failed, the retry would need these bytes.
        await _blobService.DeleteQuarantineBlobAsync(asset.QuarantineBlobPath ?? string.Empty);
        return true;
    }
}
