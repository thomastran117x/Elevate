namespace backend.main.features.clubs.search
{
    public interface IClubReindexService
    {
        Task<int> ReindexAllAsync(CancellationToken cancellationToken = default);
    }
}
