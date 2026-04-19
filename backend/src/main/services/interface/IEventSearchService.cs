using backend.main.models.documents;
using backend.main.models.search;

namespace backend.main.services.interfaces
{
    public interface IEventSearchService
    {
        Task EnsureIndexAsync();
        Task DeleteIndexAsync();
        Task IndexAsync(EventDocument document);
        Task DeleteAsync(int eventId);
        Task BulkIndexAsync(IEnumerable<EventDocument> documents);
        Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria);
    }

    public sealed record EventSearchResult(
        List<EventSearchHit> Hits,
        int TotalCount);

    public sealed record EventSearchHit(int Id, double? DistanceKm);
}
