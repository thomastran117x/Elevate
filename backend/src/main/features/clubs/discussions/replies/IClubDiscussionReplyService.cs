namespace backend.main.features.clubs.discussions.replies;

public interface IClubDiscussionReplyService
{
    Task EnsureCanReadClubAsync(int clubId, int? userId, string? userRole);
    Task<DiscussionReplyPage> GetPageAsync(
        int clubId, int discussionId, int? parentReplyId, DiscussionReplySort sort,
        string? cursor, int pageSize, int? currentUserId, string? currentUserRole);
    Task<DiscussionReplyView> CreateAsync(
        int clubId, int discussionId, int? parentReplyId, int userId, string? userRole, string content);
    Task<DiscussionReplyView> UpdateAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole, string content);
    Task<DiscussionReplyView> DeleteAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole);
    Task<DiscussionReplyReactionSummary> SetReactionAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole,
        DiscussionReplyReactionType reaction);
    Task<DiscussionReplyReactionSummary> ClearReactionAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole);
}
