using backend.main.shared.responses;

namespace backend.main.features.clubs.discussions.replies.contracts.responses;

public sealed class DiscussionReplyResponse
{
    public int Id { get; init; }
    public int DiscussionId { get; init; }
    public int? ParentReplyId { get; init; }
    public int? UserId { get; init; }
    public string? Content { get; init; }
    public AuthorInfo? Author { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int LikeCount { get; init; }
    public int DislikeCount { get; init; }
    public string? CurrentUserReaction { get; init; }
    public int DirectReplyCount { get; init; }
}

public sealed class DiscussionReplyPageResponse
{
    public IEnumerable<DiscussionReplyResponse> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
}

public sealed class DiscussionReplyReactionResponse
{
    public int ReplyId { get; init; }
    public int LikeCount { get; init; }
    public int DislikeCount { get; init; }
    public string? CurrentUserReaction { get; init; }
}
