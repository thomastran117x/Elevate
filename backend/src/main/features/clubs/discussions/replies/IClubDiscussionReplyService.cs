namespace backend.main.features.clubs.discussions.replies;

public interface IClubDiscussionReplyService
{
    Task EnsureCanReadClubAsync(int clubId, int? userId, string? userRole);
    /// <summary>
    /// As <see cref="EnsureCanReadClubAsync"/>, and additionally proves the discussion belongs
    /// to the club. Callers that key anything on the discussion id alone must use this, or a
    /// caller could pair a club they can read with a discussion from one they cannot.
    /// </summary>
    Task EnsureCanReadDiscussionAsync(int clubId, int discussionId, int? userId, string? userRole);
    Task<DiscussionReplyPage> GetPageAsync(
        int clubId, int discussionId, int? parentReplyId, DiscussionReplySort sort,
        string? cursor, int pageSize, int? currentUserId, string? currentUserRole);
    Task<DiscussionReplyView> CreateAsync(
        int clubId, int discussionId, int? parentReplyId, int userId, string? userRole, string content);
    Task<DiscussionReplyView> UpdateAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole, string content);
    /// <summary>
    /// Soft-deletes a reply: its author, staff who outrank the author, or an admin. Removing
    /// someone else's reply needs a reason and is logged.
    /// </summary>
    Task<DiscussionReplyView> DeleteAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole, string? reason = null);
    Task<DiscussionReplyReactionSummary> SetReactionAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole,
        DiscussionReplyReactionType reaction);
    Task<DiscussionReplyReactionSummary> ClearReactionAsync(
        int clubId, int discussionId, int replyId, int userId, string? userRole);
}
