using backend.main.Models;

namespace backend.main.Interfaces
{
    public interface IFollowRepository
    {
        Task<FollowClub> FollowClubAsync(int clubId, int userId);
        Task<bool> UnfollowClubAsync(int clubId, int userId);
        Task<bool> UnfollowClubAsync(int Id);
        Task<FollowClub?> IsFollowingClubAsync(int clubId, int userId);
        Task<FollowClub?> GetFollowAsync(int id);
        Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20);
        Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20);
        Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20);
    }
}
