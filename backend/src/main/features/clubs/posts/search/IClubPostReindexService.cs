namespace backend.main.features.clubs.posts.search
{
    public interface IClubPostReindexService
    {
        Task<int> ReindexAllAsync(CancellationToken cancellationToken = default);
    }
}
