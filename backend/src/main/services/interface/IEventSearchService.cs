using backend.main.models.documents;
using backend.main.models.enums;

namespace backend.main.services.interfaces
{
    public interface IEventSearchService
    {
        Task EnsureIndexAsync();
        Task DeleteIndexAsync();
        Task IndexAsync(EventDocument document);
        Task DeleteAsync(int eventId);
        Task BulkIndexAsync(IEnumerable<EventDocument> documents);
        Task<(List<int> Ids, int TotalCount)> SearchAsync(
            string search,
            bool isPrivate,
            EventStatus? status,
            int page,
            int pageSize);
    }
}
