using backend.main.features.events;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

namespace backend.main.features.events.search
{
    public class EventReindexService : IEventReindexService
    {
        private const int BatchSize = 100;
        private const int BulkIndexMaxAttempts = 3;
        private static readonly TimeSpan ReindexTimeout = TimeSpan.FromMinutes(10);

        private readonly IEventsRepository _eventsRepository;
        private readonly IEventSearchService _searchService;

        public EventReindexService(IEventsRepository eventsRepository, IEventSearchService searchService)
        {
            _eventsRepository = eventsRepository;
            _searchService = searchService;
        }

        public async Task<int> ReindexAllAsync(CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ReindexTimeout);
            var token = timeoutCts.Token;

            try
            {
                token.ThrowIfCancellationRequested();
                await _searchService.DeleteIndexAsync(token);
                await _searchService.EnsureIndexAsync(token);

                int totalIndexed = 0;
                int page = 1;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var events = await _eventsRepository.GetAllForReindexAsync(page, BatchSize, token);
                    if (events.Count == 0)
                        break;

                    var publishable = events
                        .Where(e => EventLifecyclePolicy.IsVisibleInPublicListings(e.LifecycleState))
                        .ToList();
                    var documents = publishable
                        .Select(EventSearchDocumentMapper.ToDocument)
                        .ToList();

                    await BulkIndexWithRetryAsync(documents, token);
                    totalIndexed += publishable.Count;
                    page++;

                    if (events.Count < BatchSize)
                        break;
                }

                Logger.Info($"Reindex complete. {totalIndexed} events indexed.");
                return totalIndexed;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Warn($"Event reindex exceeded the {ReindexTimeout.TotalMinutes:0} minute timeout.");
                throw new GatewayTimeoutException("Event reindex timed out.");
            }
        }

        private async Task BulkIndexWithRetryAsync(
            IReadOnlyCollection<EventDocument> documents,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _searchService.BulkIndexAsync(documents, cancellationToken);
                    return;
                }
                catch (ElasticsearchUnavailableException ex)
                    when (attempt < BulkIndexMaxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(200 * attempt);
                    Logger.Warn(
                        ex,
                        $"Event reindex bulk attempt {attempt} failed. Retrying in {delay.TotalMilliseconds:0} ms.");
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
    }
}
