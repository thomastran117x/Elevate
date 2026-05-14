using backend.main.features.clubs.posts.search;

using Polly;
using Polly.Retry;
using backend.main.shared.utilities.logger;

namespace backend.worker.clubpost_indexer;

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
        ClubPostIndexerEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ClubPostIndexerMessage? parsed = null;

        try
        {
            parsed = ClubPostIndexerMessageParser.Parse(envelope);

            await RetryPipeline.ExecuteAsync(async ct =>
            {
                if (parsed.Operation == ClubPostIndexerOperation.Delete)
                {
                    await _searchService.DeleteAsync(parsed.PostId!.Value, ct);
                }
                else
                {
                    await _searchService.IndexAsync(parsed.Document!, ct);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClubPostIndexerMessageParseException ex)
        {
            Logger.Warn(ex, "Invalid CDC club post index payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var postId = parsed?.PostId?.ToString() ?? "unknown";
            Logger.Warn(ex, $"CDC club post indexing failed for post {postId}. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }
}
