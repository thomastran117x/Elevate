using backend.main.features.clubs.follow;
using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.follow
{
    public interface IFollowService
    {
        Task<FollowClub> GetFollowAsync(int id);
        Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20);
        Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20);
        Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20);
        /// <summary>
        /// Returns a page of club members enriched with their public profile fields,
        /// plus the total member count for the club.
        /// </summary>
        Task<(IReadOnlyList<FollowClub> Members, IReadOnlyDictionary<int, UserListRecord> Users, int TotalCount)>
            GetClubMembersAsync(int clubId, int page = 1, int pageSize = 20, string? search = null);
        Task<bool> IsMemberAsync(int clubId, int userId);
        Task AddMembershipAsync(int clubId, int userId);
        Task RemoveMembershipAsync(int clubId, int userId);
    }
}


