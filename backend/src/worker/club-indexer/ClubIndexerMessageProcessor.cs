using backend.main.features.clubs.search;
using backend.main.shared.utilities.logger;

using Polly;
using Polly.Retry;

namespace backend.worker.club_indexer;

public sealed class ClubIndexerMessageProcessor
{
    private readonly IClubSearchService _searchService;
    private readonly IClubIndexerDlqPublisher _dlqPublisher;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>()
        })
        .Build();

    public ClubIndexerMessageProcessor(
        IClubSearchService searchService,
        IClubIndexerDlqPublisher dlqPublisher)
    {
        _searchService = searchService;
        _dlqPublisher = dlqPublisher;
    }

    public async Task ProcessAsync(
        ClubIndexerEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ClubIndexerMessage? parsed = null;

        try
        {
            parsed = ClubIndexerMessageParser.Parse(envelope);

            await RetryPipeline.ExecuteAsync(async ct =>
            {
                if (parsed.Operation == ClubIndexerOperation.Delete)
                {
                    await _searchService.DeleteAsync(parsed.ClubId!.Value, ct);
                    return;
                }

                await _searchService.IndexAsync(parsed.Document!, ct);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClubIndexerMessageParseException ex)
        {
            Logger.Warn(ex, "Invalid CDC club index payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var clubId = parsed?.ClubId?.ToString() ?? "unknown";
            Logger.Warn(ex, $"CDC club indexing failed for club {clubId}. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }
}
