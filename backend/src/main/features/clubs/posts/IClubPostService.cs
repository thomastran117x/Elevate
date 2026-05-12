namespace backend.main.features.clubs.posts
{
    public interface IClubPostService
    {
        Task<ClubPost> CreateAsync(int clubId, int userId, string title, string content, PostType postType, bool isPinned);
        Task<(List<ClubPost> Items, int TotalCount, string Source)> GetByClubIdAsync(
            int clubId, int? requestingUserId, string? search, PostSortBy sortBy, int page, int pageSize);
        Task<ClubPost> UpdateAsync(int clubId, int postId, int userId, string title, string content, PostType postType, bool isPinned);
        Task DeleteAsync(int clubId, int postId, int userId);
        Task<(List<ClubPost> Items, int TotalCount, string Source)> GetAllAdminAsync(
            string? search, PostSortBy sortBy, int page, int pageSize);
    }
}



