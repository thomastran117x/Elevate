namespace backend.main.services.interfaces
{
    public interface IEventReindexService
    {
        Task<int> ReindexAllAsync();
    }
}
