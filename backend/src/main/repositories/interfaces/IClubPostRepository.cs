using backend.main.models.core;

namespace backend.main.repositories.interfaces
{
    public interface IClubPostRepository
    {
        Task<ClubPost> CreateAsync(ClubPost post);
        Task<ClubPost?> GetByIdAsync(int id);
        Task<List<ClubPost>> GetByClubIdAsync(int clubId, string? search, int page, int pageSize);
        Task<int> CountByClubIdAsync(int clubId, string? search);
        Task<List<ClubPost>> GetAllAsync(string? search, int page, int pageSize);
        Task<int> CountAllAsync(string? search);
        Task<ClubPost?> UpdateAsync(int id, ClubPost updated);
        Task<bool> DeleteAsync(int id);
        Task IncrementViewCountAsync(IEnumerable<int> postIds);
    }
}
