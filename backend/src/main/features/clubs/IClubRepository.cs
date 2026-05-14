using backend.main.features.clubs.search;

namespace backend.main.features.clubs
{
    public interface IClubRepository
    {
        Task<Club> CreateAsync(Club club);
        Task<Club?> GetByIdAsync(int id);
        Task<IEnumerable<Club>> GetAllAsync(int page = 1, int pageSize = 20);
        Task<Club?> GetByUserIdAsync(int userId);
        Task<Club?> UpdateAsync(int id, Club updatedClub);
        Task<bool> UpdatePartialAsync(int id, Action<Club> patch);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<(List<Club> Items, int TotalCount)> SearchAsync(ClubSearchCriteria criteria);
        Task<List<Club>> GetByIdsAsync(IEnumerable<int> ids);
        Task<List<Club>> GetAllForReindexAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    }
}

