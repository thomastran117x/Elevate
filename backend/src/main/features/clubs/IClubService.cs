using backend.main.features.clubs;

using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.clubs.versions;

namespace backend.main.features.clubs
{
    public interface IClubService
    {
        Task<(List<Club> Clubs, int TotalCount, string Source)> GetAllClubs(ClubSearchCriteria criteria);
        Task<Club> GetClub(int clubId);
        Task<List<Club>> GetManagedClubsAsync(int userId);
        Task<ClubAccessInfo> GetClubAccessAsync(int clubId, int? userId, string? userRole = null);
        Task<Dictionary<int, ClubAccessInfo>> GetClubAccessMapAsync(
            IEnumerable<int> clubIds,
            int? userId,
            string? userRole = null);
        Task<bool> CanManageClubAsync(int clubId, int userId, string? userRole = null);
        Task<bool> HasClubStaffAccessAsync(int clubId, int userId, string? userRole = null);
        Task<bool> CanManageClubPostsAsync(int clubId, int userId, string? userRole = null);
        Task<bool> CanManageEventMediaAsync(int clubId, int userId, string? userRole = null);
        Task<bool> IsClubOwnerAsync(int clubId, int userId, string? userRole = null);
        Task<Club> CreateClub(string name, int userId, string description, string clubtype, IFormFile clubimage, string? phone = null, string? email = null);
        Task<Club> UpdateClub(int clubId, int userId, string userRole, string name, string description, string clubtype, IFormFile clubimage, string? phone = null, string? email = null);
        Task<List<Club>> GetClubsByIdsAsync(IEnumerable<int> clubIds);
        Task DeleteClub(int clubId, int userId);
        Task<IReadOnlyList<ClubStaff>> GetStaffAsync(int clubId, int userId, string userRole);
        Task<ClubStaff> AddStaffAsync(int clubId, int targetUserId, ClubStaffRole role, int actorUserId, string actorUserRole);
        Task RemoveStaffAsync(int clubId, int targetUserId, int actorUserId, string actorUserRole);
        Task<Club> TransferOwnershipAsync(int clubId, int newOwnerUserId, int actorUserId, string actorUserRole);
        Task JoinClubAsync(int clubId, int userId);
        Task LeaveClubAsync(int clubId, int userId);
        Task EventCreatedAsync(int clubId, int eventId);
        Task EventDeletedAsync(int clubId, int eventId);
        Task<(List<ClubVersionHistoryItem> Items, int TotalCount)> GetVersionHistoryAsync(
            int clubId,
            int userId,
            string userRole,
            int page = 1,
            int pageSize = 20);
        Task<ClubVersionDetail> GetVersionDetailAsync(
            int clubId,
            int versionNumber,
            int userId,
            string userRole);
        Task<ClubRollbackResult> RollbackToVersionAsync(
            int clubId,
            int versionNumber,
            int userId,
            string userRole);
    }
}

