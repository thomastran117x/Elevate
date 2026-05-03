using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Polly;
using Polly.Retry;

namespace backend.worker.event_indexer;

public sealed class EventIndexerMessageProcessor
{
    private readonly IEventSearchService _searchService;
    private readonly IEventIndexerDlqPublisher _dlqPublisher;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>()
        })
        .Build();

    public EventIndexerMessageProcessor(
        IEventSearchService searchService,
        IEventIndexerDlqPublisher dlqPublisher)
    {
        _searchService = searchService;
        _dlqPublisher = dlqPublisher;
    }

    public async Task ProcessAsync(
        EventIndexerEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        EventIndexerMessage? parsed = null;

        try
        {
            parsed = EventIndexerMessageParser.Parse(envelope);

            await RetryPipeline.ExecuteAsync(async ct =>
            {
                if (parsed.Operation == EventIndexerOperation.Delete)
                {
                    await _searchService.DeleteAsync(parsed.EventId!.Value, ct);
                    return;
                }

                await _searchService.IndexAsync(parsed.Document!, ct);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EventIndexerMessageParseException ex)
        {
            Logger.Warn(ex, "Invalid CDC event index payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var eventId = parsed?.EventId?.ToString() ?? "unknown";
            Logger.Warn(ex, $"CDC event indexing failed for event {eventId}. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }
}
