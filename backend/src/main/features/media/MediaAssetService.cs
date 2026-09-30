using backend.main.features.cache;
using backend.main.features.events.contracts.responses;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// <see cref="IMediaAssetService"/> with quarantine on: uploads land in the private container, and
/// attaching one runs <see cref="MediaValidationPipeline"/> and records the result on its
/// <see cref="MediaAsset"/>. A URL reaches an event, club or profile only after its asset is
/// <see cref="MediaAssetStatus.Ready"/>, which is what keeps unvalidated bytes off every public
/// page without a status filter on any read path.
/// </summary>
public sealed class MediaAssetService : IMediaAssetService
{
    /// <summary>
    /// How long a claim may sit in Processing before another attach presumes its holder died. The
    /// pipeline takes well under a second, and a request that waits for a processing slot gives
    /// up after seconds, so this is generous by an order of magnitude.
    /// </summary>
    internal static readonly TimeSpan StaleProcessingAfter = TimeSpan.FromMinutes(5);

    internal const string StillProcessingMessage = "This image is still being checked. Try again in a moment.";
    internal const string UnderReviewMessage = "This image is waiting for review.";
    internal const string GenericRejectionMessage = "This image couldn't be published.";

    // Reloads after a lost race. Each lap means another request moved the asset first, so a
    // handful covers every real interleaving; running out is reported as still processing.
    private const int MaxStateReads = 5;

    private readonly AppDatabaseContext _db;
    private readonly IMediaAssetRepository _repository;
    private readonly IAzureBlobService _blobService;
    private readonly ICacheService _cache;
    private readonly MediaValidationPipeline _pipeline;
    private readonly TimeProvider _timeProvider;

    public MediaAssetService(
        AppDatabaseContext db,
        IMediaAssetRepository repository,
        IAzureBlobService blobService,
        ICacheService cache,
        MediaValidationPipeline pipeline,
        TimeProvider timeProvider)
    {
        _db = db;
        _repository = repository;
        _blobService = blobService;
        _cache = cache;
        _pipeline = pipeline;
        _timeProvider = timeProvider;
    }

    public async Task<PresignedUploadResponse> IssueUploadAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var upload = await _blobService.GenerateQuarantineUploadUrlAsync(
            request.BlobPathPrefix, request.FileName, request.ContentType);

        var now = UtcNow();
        var asset = new MediaAsset
        {
            PublicId = Guid.CreateVersion7(),
            OwnerUserId = request.UserId,
            ClubId = request.ClubId,
            EventId = request.EventId,
            Status = MediaAssetStatus.PendingUpload,
            Origin = MediaAssetOrigin.Upload,
            QuarantineBlobPath = upload.QuarantineBlobPath,
            PublicUrl = upload.PublicUrl,
            DeclaredContentType = upload.ContentType,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Nothing else is pending on this context on the presigned-URL path, so saving here
        // commits only the new row.
        await _repository.AddAsync(asset);
        await _db.SaveChangesAsync(cancellationToken);

        return new PresignedUploadResponse
        {
            UploadUrl = upload.UploadUrl,
            PublicUrl = upload.PublicUrl,
            ExpiresAt = upload.ExpiresAt,
            MediaAssetId = asset.PublicId
        };
    }

    public async Task<BlobUploadIntent> AttachAsync(
        int userId,
        string imageUrl,
        string subject,
        Action<BlobUploadIntent>? checkScope = null,
        CancellationToken cancellationToken = default)
    {
        var intent = await BlobUploadIntentValidator.ResolveIntentAsync(
            _blobService, _cache, userId, imageUrl, subject);

        checkScope?.Invoke(intent);

        if (intent.MediaAssetId is not Guid mediaAssetId)
        {
            // Issued before quarantine was switched on, so the bytes are in the public container
            // and get the checks they would have got then.
            await BlobUploadIntentValidator.RequireAcceptableBlobAsync(_blobService, imageUrl, intent, subject);
            return intent;
        }

        await EnsureReadyAsync(mediaAssetId, userId, imageUrl, subject, cancellationToken);
        return intent;
    }

    /// <summary>
    /// Drives the asset to Ready, or throws the reason it cannot get there. Every step re-reads
    /// the row and moves it with a conditional update, so concurrent attaches of the same upload
    /// agree on one outcome and only one of them does the work.
    /// </summary>
    private async Task EnsureReadyAsync(
        Guid mediaAssetId,
        int userId,
        string imageUrl,
        string subject,
        CancellationToken cancellationToken)
    {
        for (var read = 0; read < MaxStateReads; read++)
        {
            var asset = await _repository.GetByPublicIdAsync(mediaAssetId, cancellationToken)
                ?? throw new BadRequestException("Image upload is invalid or expired. Please upload the image again.");

            if (asset.OwnerUserId != userId || !string.Equals(asset.PublicUrl, imageUrl, StringComparison.Ordinal))
                throw new BadRequestException("Image upload is invalid or does not belong to this organizer.");

            switch (asset.Status)
            {
                case MediaAssetStatus.Ready:
                    // Attaching an upload twice — a retried save, or the same image on a second
                    // event within the intent's lifetime — is allowed, as it always has been.
                    return;

                case MediaAssetStatus.Rejected:
                    throw new BadRequestException(asset.RejectionReason ?? GenericRejectionMessage);

                case MediaAssetStatus.NeedsReview:
                    throw new ConflictException(UnderReviewMessage);

                case MediaAssetStatus.Processing:
                    if (asset.UpdatedAt > UtcNow() - StaleProcessingAfter)
                        throw new ConflictException(StillProcessingMessage);

                    // The request that claimed it is presumed dead. Releasing rather than
                    // re-claiming in one step keeps AttemptCount honest and lets the next lap
                    // go through the same claim as everyone else.
                    Logger.Warn($"[MediaAssetService] Releasing stale claim on media asset {asset.PublicId}.");
                    await _repository.TryTransitionAsync(
                        asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Uploaded, cancellationToken: cancellationToken);
                    continue;

                case MediaAssetStatus.PendingUpload:
                    // The server learns the PUT happened only by looking. No blob means the
                    // upload failed or never started, and the asset stays pending so a retried
                    // PUT within the SAS window can still complete it.
                    if (await _blobService.InspectQuarantineBlobAsync(
                            asset.QuarantineBlobPath ?? string.Empty, cancellationToken: cancellationToken) == null)
                    {
                        throw new BadRequestException(ImageUploadGate.DidNotCompleteMessage);
                    }

                    await _repository.TryTransitionAsync(
                        asset.Id, MediaAssetStatus.PendingUpload, MediaAssetStatus.Uploaded, cancellationToken: cancellationToken);
                    continue;

                case MediaAssetStatus.Uploaded:
                    var claimed = await _repository.TryTransitionAsync(
                        asset.Id,
                        MediaAssetStatus.Uploaded,
                        MediaAssetStatus.Processing,
                        new MediaAssetChanges { CountAttempt = true },
                        cancellationToken);

                    if (!claimed)
                        continue;

                    await ProcessClaimedAsync(asset, subject, cancellationToken);
                    return;
            }
        }

        throw new ConflictException(StillProcessingMessage);
    }

    private async Task ProcessClaimedAsync(MediaAsset asset, string subject, CancellationToken cancellationToken)
    {
        MediaValidationOutcome outcome;
        try
        {
            outcome = await _pipeline.RunAsync(
                asset.QuarantineBlobPath ?? string.Empty,
                asset.PublicUrl!,
                asset.DeclaredContentType,
                subject,
                cancellationToken);
        }
        catch
        {
            // Not a verdict on the bytes — no processing slot, a storage fault, a cancelled
            // request. Hand the asset back so the user's retry can claim it straight away rather
            // than waiting out the stale-claim window. CancellationToken.None: the release must
            // happen even when it was the request's own token that fired.
            await ReleaseClaimAsync(asset);
            throw;
        }

        if (outcome.Accepted)
        {
            await _repository.TryTransitionAsync(
                asset.Id,
                MediaAssetStatus.Processing,
                MediaAssetStatus.Ready,
                new MediaAssetChanges
                {
                    ContentType = outcome.ContentType,
                    Width = outcome.Width,
                    Height = outcome.Height,
                    ByteSize = outcome.ByteSize,
                    ValidatedAt = UtcNow(),
                    ClearQuarantineBlobPath = true
                },
                CancellationToken.None);

            // Only now: had the Ready write failed, the retry would need these bytes.
            await _blobService.DeleteQuarantineBlobAsync(asset.QuarantineBlobPath ?? string.Empty);
            return;
        }

        var reason = outcome.RejectionReason ?? GenericRejectionMessage;
        await _repository.TryTransitionAsync(
            asset.Id,
            MediaAssetStatus.Processing,
            MediaAssetStatus.Rejected,
            new MediaAssetChanges { RejectionReason = reason, ClearQuarantineBlobPath = true },
            CancellationToken.None);

        await _blobService.DeleteQuarantineBlobAsync(asset.QuarantineBlobPath ?? string.Empty);

        throw new BadRequestException(reason);
    }

    /// <remarks>
    /// Swallows its own failure: it runs while another exception is already on its way out, and
    /// that one says what actually went wrong. An asset left in Processing is recovered by the
    /// stale-claim path anyway.
    /// </remarks>
    private async Task ReleaseClaimAsync(MediaAsset asset)
    {
        try
        {
            await _repository.TryTransitionAsync(
                asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Uploaded, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"[MediaAssetService] Could not release the claim on media asset {asset.PublicId}.");
        }
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
