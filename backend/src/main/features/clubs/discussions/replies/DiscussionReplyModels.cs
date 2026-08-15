using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.discussions.replies;

public enum DiscussionReplySort
{
    Newest = 0,
    Oldest = 1
}

public sealed record DiscussionReplyCursor(DateTime CreatedAt, int Id);

public sealed record DiscussionReplyPage(
    IReadOnlyList<DiscussionReplyView> Items,
    int TotalCount,
    string? NextCursor,
    bool HasMore);

public sealed record DiscussionReplyView(
    ClubDiscussionReply Reply,
    UserListRecord? Author,
    int LikeCount,
    int DislikeCount,
    DiscussionReplyReactionType? CurrentUserReaction,
    int DirectReplyCount);

public sealed record DiscussionReplyReactionSummary(
    int LikeCount,
    int DislikeCount,
    DiscussionReplyReactionType? CurrentUserReaction);
