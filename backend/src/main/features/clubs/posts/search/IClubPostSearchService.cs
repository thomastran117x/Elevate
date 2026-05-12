using backend.main.models.enums;

namespace backend.main.features.clubs.posts.search
{
    public interface IClubPostSearchService
    {
        Task EnsureIndexAsync(CancellationToken cancellationToken = default);
        Task DeleteIndexAsync(CancellationToken cancellationToken = default);
        Task IndexAsync(ClubPostDocument document, CancellationToken cancellationToken = default);
        Task DeleteAsync(int postId, CancellationToken cancellationToken = default);
        Task BulkIndexAsync(IEnumerable<ClubPostDocument> documents, CancellationToken cancellationToken = default);
        Task<(List<int> Ids, int TotalCount)> SearchByClubAsync(int clubId, string search, PostSortBy sortBy, int page, int pageSize);
        Task<(List<int> Ids, int TotalCount)> SearchAllAsync(string search, PostSortBy sortBy, int page, int pageSize);
    }
}
