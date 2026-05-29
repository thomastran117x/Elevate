using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.posts
{
    public interface IClubPostService
    {
        Task<ClubPost> CreateAsync(int clubId, int userId, string userRole, string title, string content, PostType postType, bool isPinned);
        Task<(ClubPost Post, UserListRecord? Author)> GetByIdAsync(
            int clubId, int postId, int? requestingUserId, string? requestingUserRole);
        Task<(List<ClubPost> Items, int TotalCount, string Source, Dictionary<int, UserListRecord> Authors)> GetByClubIdAsync(
            int clubId, int? requestingUserId, string? requestingUserRole, string? search, PostSortBy sortBy, int page, int pageSize);
        Task<ClubPost> UpdateAsync(int clubId, int postId, int userId, string userRole, string title, string content, PostType postType, bool isPinned);
        Task DeleteAsync(int clubId, int postId, int userId, string userRole);
        Task<(List<ClubPost> Items, int TotalCount, string Source)> GetAllAdminAsync(
            string? search, PostSortBy sortBy, int page, int pageSize);
    }
}
