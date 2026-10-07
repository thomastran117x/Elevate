using backend.main.shared.providers.messages;
using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

using Polly;
using Polly.Retry;

namespace backend.worker.media_worker;

/// <summary>
/// Runs <see cref="MediaValidationPipeline"/> on one request and reports the outcome to the API.
/// Knows nothing about what the image is attached to, and never reads or writes the database.
/// </summary>
public sealed class MediaWorkerMessageProcessor
{
    private readonly MediaValidationPipeline _pipeline;
    private readonly IMediaWorkerDlqPublisher _dlqPublisher;
    private readonly IMediaValidationStatusPublisher _statusPublisher;

    // Rejections are outcomes, not exceptions, so this retries only what is not a verdict on the
    // bytes: no processing slot free, a storage fault.
    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>()
        })
        .Build();

    public MediaWorkerMessageProcessor(
        MediaValidationPipeline pipeline,
        IMediaWorkerDlqPublisher dlqPublisher,
        IMediaValidationStatusPublisher statusPublisher)
    {
        _pipeline = pipeline;
        _dlqPublisher = dlqPublisher;
        _statusPublisher = statusPublisher;
    }

    public async Task ProcessAsync(
        MediaWorkerEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        MediaValidationRequestMessage? request = null;
        MediaValidationOutcome outcome;

        try
        {
            request = MediaWorkerMessageParser.Parse(envelope);

            outcome = await RetryPipeline.ExecuteAsync(
                async ct => await _pipeline.RunAsync(
                    request.QuarantineBlobPath,
                    request.PublicUrl,
                    request.DeclaredContentType,
                    request.Subject,
                    ct),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MediaWorkerMessageParseException ex)
        {
            Logger.Warn(ex, "Invalid media validation payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
            return;
        }
        catch (Exception ex)
        {
            // The asset stays Processing; the API's reconciler re-drives it once the claim is stale.
            var assetId = request?.MediaAssetId.ToString() ?? "unknown";
            Logger.Warn(ex, $"Media validation failed for media asset {assetId}. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
            return;
        }

        // Outside the try: a result that cannot be reported leaves the offset uncommitted, so the
        // request runs again. That is safe — the pipeline publishes the same bytes to the same
        // URL, and the API records an outcome only once per claim.
        await _statusPublisher.PublishAsync(MediaValidationResultMessage.From(request, outcome), cancellationToken);

        Logger.Info(outcome.Accepted
            ? $"Media asset {request.MediaAssetId} passed validation under claim {request.Attempt}."
            : $"Media asset {request.MediaAssetId} was refused under claim {request.Attempt}: {outcome.RejectionReason}");
    }
}
