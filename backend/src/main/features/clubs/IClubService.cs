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
        Task<Club> CreateClub(string name, int userId, string description, string clubtype, string clubImageUrl, string? phone = null, string? email = null);
        Task<Club> UpdateClub(int clubId, int userId, string userRole, string name, string description, string clubtype, string clubImageUrl, string? phone = null, string? email = null);
        Task<List<Club>> GetClubsByIdsAsync(IEnumerable<int> clubIds);
        Task DeleteClub(int clubId, int userId);
        Task<IReadOnlyList<ClubStaff>> GetStaffAsync(int clubId, int userId, string userRole);
        Task<ClubStaff> AddStaffAsync(int clubId, int targetUserId, ClubStaffRole role, int actorUserId, string actorUserRole);
        /// <summary>
        /// Grants a staff role as the result of an accepted invitation. Unlike <see cref="AddStaffAsync"/>
        /// this skips the owner-actor authorization check (the invited user is the actor) but still
        /// validates the target exists and the club owner is not re-added. Idempotent: returns the
        /// existing assignment when the user already holds a staff role.
        /// </summary>
        Task<ClubStaff> GrantStaffFromInvitationAsync(int clubId, int targetUserId, ClubStaffRole role, int grantedByUserId);
        Task<bool> IsClubStaffMemberAsync(int clubId, int targetUserId);
        Task RemoveStaffAsync(int clubId, int targetUserId, int actorUserId, string actorUserRole);
        Task<Club> TransferOwnershipAsync(int clubId, int newOwnerUserId, int actorUserId, string actorUserRole);
        Task JoinClubAsync(int clubId, int userId);
        /// <summary>
        /// Grants club membership as the result of an accepted member invitation or a redeemed
        /// invite link. Unlike <see cref="JoinClubAsync"/> this bypasses the <c>isPrivate</c> gate
        /// (the invitation is the authorization) and is idempotent: a no-op when the user is already
        /// a member.
        /// </summary>
        Task GrantMembershipFromInvitationAsync(int clubId, int userId);
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
