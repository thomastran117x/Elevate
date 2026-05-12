using backend.main.features.clubs.follow;

namespace backend.main.features.clubs.follow
{
    public interface IFollowService
    {
        Task<FollowClub> GetFollowAsync(int id);
        Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20);
        Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20);
        Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20);
        Task<bool> IsMemberAsync(int clubId, int userId);
        Task AddMembershipAsync(int clubId, int userId);
        Task RemoveMembershipAsync(int clubId, int userId);
    }
}


