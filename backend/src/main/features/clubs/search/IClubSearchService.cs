namespace backend.main.features.clubs.search
{
    public interface IClubSearchService
    {
        Task EnsureIndexAsync(CancellationToken cancellationToken = default);
        Task DeleteIndexAsync(CancellationToken cancellationToken = default);
        Task IndexAsync(ClubDocument document, CancellationToken cancellationToken = default);
        Task DeleteAsync(int clubId, CancellationToken cancellationToken = default);
        Task BulkIndexAsync(IEnumerable<ClubDocument> documents, CancellationToken cancellationToken = default);
        Task<ClubSearchResult> SearchAsync(ClubSearchCriteria criteria);
    }

    public sealed record ClubSearchResult(
        List<ClubSearchHit> Hits,
        int TotalCount);

    public sealed record ClubSearchHit(int Id);
}
