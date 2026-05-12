namespace backend.main.features.events.search
{
    public interface IEventReindexService
    {
        Task<int> ReindexAllAsync(CancellationToken cancellationToken = default);
    }
}
