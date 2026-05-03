using backend.main.models.core;
using backend.main.models.enums;
using backend.main.models.search;

namespace backend.main.repositories.interfaces
{
    public interface IEventsRepository
    {
        Task<Events> CreateAsync(Events events);
        Task<Events?> GetByIdAsync(int id);
        Task<IEnumerable<Events>> GetAllAsync(int page = 1, int pageSize = 20);
        Task<Events?> GetByClubIdAsync(int clubId);
        Task<Events?> UpdateAsync(int id, Events events);
        Task<bool> UpdatePartialAsync(int id, Action<Events> patch);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<(List<Events> Items, int TotalCount)> SearchAsync(EventSearchCriteria criteria);
        Task<List<Events>> GetByIdsAsync(IEnumerable<int> ids);
        Task<List<Events>> GetAllForReindexAsync(int page, int pageSize, CancellationToken cancellationToken = default);
        Task<List<Events>> CreateManyAsync(IEnumerable<Events> events);
        Task<int> UpdateManyAsync(IEnumerable<(int id, Action<Events> patch)> updates);
        Task<int> DeleteManyAsync(IEnumerable<int> ids);
        Task IncrementRegistrationCountAsync(int eventId, int delta);
    }
}
