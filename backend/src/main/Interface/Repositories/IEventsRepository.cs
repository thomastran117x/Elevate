using backend.main.Models;

namespace backend.main.Interfaces
{
    public interface IEventsRepository
    {
        Task<Events> CreateAsync(Events events);
        Task<Events?> GetByIdAsync(int id);
        Task<IEnumerable<Events>> GetAllAsync(int page = 1, int pageSize = 20);
        Task<Events> GetByClubIdAsync(int clubId);
        Task<Events?> UpdateAsync(int id, Events events);
        Task<bool> UpdatePartialAsync(int id, Action<Events> patch);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<List<Events>> SearchAsync(
            string? search,
            bool isPrivate,
            bool isAvaliable,
            int page = 1,
            int pageSize = 20);
        Task<List<Events>> GetByIdsAsync(IEnumerable<int> ids);
    }
}
