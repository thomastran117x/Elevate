namespace backend.main.features.clubs.posts.comments;

public interface IPostCommentService
{
    Task EnsureCanReadPostAsync(int clubId, int postId, int? userId, string? userRole);
    Task<PostCommentPage> GetPageAsync(
        int clubId, int postId, int? parentCommentId, PostCommentSort sort,
        string? cursor, int pageSize, int? currentUserId, string? currentUserRole);
    Task<PostCommentView> CreateAsync(
        int clubId, int postId, int? parentCommentId, int userId, string? userRole, string content);
    Task<PostCommentView> UpdateAsync(
        int clubId, int postId, int commentId, int userId, string? userRole, string content);
    /// <summary>
    /// Soft-deletes a comment: its author, staff who outrank the author, or an admin. Removing
    /// someone else's comment needs a reason and is logged.
    /// </summary>
    Task<PostCommentView> DeleteAsync(
        int clubId, int postId, int commentId, int userId, string? userRole, string? reason = null);
    Task<PostCommentReactionSummary> SetReactionAsync(
        int clubId, int postId, int commentId, int userId, string? userRole,
        PostCommentReactionType reaction);
    Task<PostCommentReactionSummary> ClearReactionAsync(
        int clubId, int postId, int commentId, int userId, string? userRole);
}
