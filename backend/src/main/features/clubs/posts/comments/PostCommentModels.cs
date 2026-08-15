using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.posts.comments;

public enum PostCommentSort
{
    Newest = 0,
    Oldest = 1
}

public sealed record PostCommentCursor(DateTime CreatedAt, int Id);

public sealed record PostCommentPage(
    IReadOnlyList<PostCommentView> Items,
    int TotalCount,
    string? NextCursor,
    bool HasMore);

public sealed record PostCommentView(
    PostComment Comment,
    UserListRecord? Author,
    int LikeCount,
    int DislikeCount,
    PostCommentReactionType? CurrentUserReaction,
    int DirectReplyCount);

public sealed record PostCommentReactionSummary(
    int LikeCount,
    int DislikeCount,
    PostCommentReactionType? CurrentUserReaction);
