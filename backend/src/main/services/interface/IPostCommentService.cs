using backend.main.models.core;

namespace backend.main.services.interfaces
{
    public interface IPostCommentService
    {
        Task<PostComment> CreateAsync(int clubId, int postId, int userId, string content);
        Task<(List<PostComment> Items, int TotalCount)> GetByPostIdAsync(int clubId, int postId, int page, int pageSize);
        Task<PostComment> UpdateAsync(int postId, int commentId, int userId, string content);
        Task DeleteAsync(int postId, int commentId, int userId);
    }
}
