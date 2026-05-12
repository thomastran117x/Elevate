using backend.main.features.events.search;

namespace backend.main.features.events.search
{
    public interface IEventSearchService
    {
        Task EnsureIndexAsync(CancellationToken cancellationToken = default);
        Task DeleteIndexAsync(CancellationToken cancellationToken = default);
        Task IndexAsync(EventDocument document, CancellationToken cancellationToken = default);
        Task DeleteAsync(int eventId, CancellationToken cancellationToken = default);
        Task BulkIndexAsync(IEnumerable<EventDocument> documents, CancellationToken cancellationToken = default);
        Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria);
    }

    public sealed record EventSearchResult(
        List<EventSearchHit> Hits,
        int TotalCount);

    public sealed record EventSearchHit(int Id, double? DistanceKm);
}
