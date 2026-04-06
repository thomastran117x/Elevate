using backend.main.models.core;

namespace backend.main.repositories.interfaces
{
    public interface IPostCommentRepository
    {
        Task<PostComment> CreateAsync(PostComment comment);
        Task<PostComment?> GetByIdAsync(int id);
        Task<List<PostComment>> GetByPostIdAsync(int postId, int page, int pageSize);
        Task<int> CountByPostIdAsync(int postId);
        Task<PostComment?> UpdateAsync(int id, PostComment updated);
        Task<bool> DeleteAsync(int id);
    }
}
