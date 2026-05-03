using System.Text.Json;

using backend.main.consumers;
using backend.main.dtos.messages;
using backend.main.publishers.implementation;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Polly;
using Polly.Retry;

namespace backend.worker.event_indexer;

public sealed class ClubPostIndexerMessageProcessor
{
    private readonly IClubPostSearchService _searchService;
    private readonly IClubPostIndexerDlqPublisher _dlqPublisher;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>()
        })
        .Build();

    public ClubPostIndexerMessageProcessor(
        IClubPostSearchService searchService,
        IClubPostIndexerDlqPublisher dlqPublisher)
    {
        _searchService = searchService;
        _dlqPublisher = dlqPublisher;
    }

    public async Task ProcessAsync(
        EventIndexerEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ClubPostIndexEvent? evt = null;

        try
        {
            evt = JsonSerializer.Deserialize<ClubPostIndexEvent>(envelope.Payload, JsonOptions.Default)
                ?? throw new ElasticsearchIndexMessageValidationException(
                    "Club post index payload could not be deserialized.");

            await RetryPipeline.ExecuteAsync(async ct =>
            {
                if (ElasticsearchIndexMessageValidator.IsDeleteOperation(evt.Operation))
                {
                    ElasticsearchIndexMessageValidator.ValidateDelete(evt);
                    await _searchService.DeleteAsync(evt.PostId, ct);
                }
                else
                {
                    var document = ElasticsearchIndexMessageValidator.ToClubPostDocument(evt);
                    await _searchService.IndexAsync(document, ct);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ElasticsearchIndexMessageValidationException ex)
        {
            Logger.Warn(ex, "Invalid club post index payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Malformed club post index payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var postId = evt?.PostId.ToString() ?? "unknown";
            Logger.Warn(ex, $"Club post indexing failed for post {postId}. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }
}
