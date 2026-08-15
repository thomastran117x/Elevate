namespace backend.main.features.clubs.posts.comments;

public interface IPostCommentRepository
{
    Task<PostComment> CreateAsync(PostComment comment);
    Task<PostComment?> GetByIdAsync(int id);
    Task<(List<PostComment> Items, bool HasMore)> GetPageAsync(
        int postId, int? parentCommentId, PostCommentSort sort,
        PostCommentCursor? cursor, int pageSize);
    Task<int> CountByParentAsync(int postId, int? parentCommentId);
    Task<Dictionary<int, int>> CountByPostIdsAsync(IEnumerable<int> postIds);
    Task<Dictionary<int, int>> GetDirectReplyCountsAsync(IEnumerable<int> commentIds);
    Task<Dictionary<int, PostCommentReactionSummary>> GetReactionSummariesAsync(
        IEnumerable<int> commentIds, int? currentUserId);
    Task<PostComment?> UpdateAsync(int id, string content);
    Task<PostComment?> SoftDeleteAsync(int id);
    Task<PostCommentReactionSummary> SetReactionAsync(
        int commentId, int userId, PostCommentReactionType reaction);
    Task<PostCommentReactionSummary> ClearReactionAsync(int commentId, int userId);
}
