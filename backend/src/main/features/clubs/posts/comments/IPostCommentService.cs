using backend.main.features.clubs.posts.comments;
using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.posts.comments
{
    public interface IPostCommentService
    {
        Task<PostComment> CreateAsync(int clubId, int postId, int userId, string content);
        Task<(List<PostComment> Items, int TotalCount, Dictionary<int, UserListRecord> Authors)> GetByPostIdAsync(int clubId, int postId, int page, int pageSize);
        Task<PostComment> UpdateAsync(int postId, int commentId, int userId, string content);
        Task DeleteAsync(int postId, int commentId, int userId);
    }
}
