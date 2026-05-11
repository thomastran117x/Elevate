using backend.main.models.documents;
using backend.main.shared.exceptions.http;
using backend.main.Mappers;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

namespace backend.main.services.implementation
{
    public class EventReindexService : IEventReindexService
    {
        private const int BatchSize = 100;
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
                    if (events.Count == 0) break;

                    var documents = events.Select(EventSearchDocumentMapper.ToDocument);

                    await _searchService.BulkIndexAsync(documents, token);
                    totalIndexed += events.Count;
                    page++;

                    if (events.Count < BatchSize) break;
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
    }
}
