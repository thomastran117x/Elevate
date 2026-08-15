namespace backend.main.features.clubs.discussions.replies;

public interface IClubDiscussionReplyRepository
{
    Task<ClubDiscussionReply> CreateAsync(ClubDiscussionReply reply);
    Task<ClubDiscussionReply?> GetByIdAsync(int id);
    Task<(List<ClubDiscussionReply> Items, bool HasMore)> GetPageAsync(
        int discussionId, int? parentReplyId, DiscussionReplySort sort,
        DiscussionReplyCursor? cursor, int pageSize);
    Task<int> CountByParentAsync(int discussionId, int? parentReplyId);
    Task<Dictionary<int, int>> CountByDiscussionIdsAsync(IEnumerable<int> discussionIds);
    Task<Dictionary<int, int>> GetDirectReplyCountsAsync(IEnumerable<int> replyIds);
    Task<Dictionary<int, DiscussionReplyReactionSummary>> GetReactionSummariesAsync(
        IEnumerable<int> replyIds, int? currentUserId);
    Task<ClubDiscussionReply?> UpdateAsync(int id, string content);
    Task<ClubDiscussionReply?> SoftDeleteAsync(int id);
    Task<DiscussionReplyReactionSummary> SetReactionAsync(
        int replyId, int userId, DiscussionReplyReactionType reaction);
    Task<DiscussionReplyReactionSummary> ClearReactionAsync(int replyId, int userId);
}
