using backend.main.models.documents;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

namespace backend.main.services.implementation
{
    public class EventReindexService : IEventReindexService
    {
        private const int BatchSize = 100;

        private readonly IEventsRepository _eventsRepository;
        private readonly IEventSearchService _searchService;

        public EventReindexService(IEventsRepository eventsRepository, IEventSearchService searchService)
        {
            _eventsRepository = eventsRepository;
            _searchService = searchService;
        }

        public async Task<int> ReindexAllAsync()
        {
            await _searchService.DeleteIndexAsync();
            await _searchService.EnsureIndexAsync();

            int totalIndexed = 0;
            int page = 1;

            while (true)
            {
                var events = await _eventsRepository.GetAllForReindexAsync(page, BatchSize);
                if (events.Count == 0) break;

                var documents = events.Select(e => new EventDocument
                {
                    Id = e.Id,
                    ClubId = e.ClubId,
                    Name = e.Name,
                    Description = e.Description,
                    Location = e.Location,
                    IsPrivate = e.isPrivate,
                    StartTime = e.StartTime,
                    EndTime = e.EndTime,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                });

                await _searchService.BulkIndexAsync(documents);
                totalIndexed += events.Count;
                page++;

                if (events.Count < BatchSize) break;
            }

            Logger.Info($"Reindex complete. {totalIndexed} events indexed.");
            return totalIndexed;
        }
    }
}
