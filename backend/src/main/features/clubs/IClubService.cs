using backend.main.features.clubs;

using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.versions;

namespace backend.main.features.clubs
{
    public interface IClubService
    {
        Task<List<Club>> GetAllClubs(
            string? search = null,
            int page = 1,
            int pageSize = 20
        );
        Task<Club> GetClub(int clubId);
        Task<Club> GetClubByUser(int userId);
        Task<Club> CreateClub(string name, int userId, string description, string clubtype, IFormFile clubimage, string? phone = null, string? email = null);
        Task<Club> UpdateClub(int clubId, int userId, string userRole, string name, string description, string clubtype, IFormFile clubimage, string? phone = null, string? email = null);
        Task<List<Club>> GetClubsByIdsAsync(IEnumerable<int> clubIds);
        Task DeleteClub(int clubId, int userId);
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

