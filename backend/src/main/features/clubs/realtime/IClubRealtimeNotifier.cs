using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.features.clubs.posts.comments.contracts.responses;

namespace backend.main.features.clubs.realtime;

/// <summary>
/// Server-to-client publishing for club realtime events. Replaces the per-feature SSE
/// event brokers.
/// </summary>
/// <remarks>
/// Event names are carried over unchanged from the SSE contract so the client-side
/// mapping did not have to be rewritten alongside the transport.
/// </remarks>
public interface IClubRealtimeNotifier
{
    Task ReplyCreatedAsync(int clubId, DiscussionReplyResponse reply);

    Task ReplyUpdatedAsync(int clubId, DiscussionReplyResponse reply);

    Task ReplyDeletedAsync(int clubId, DiscussionReplyResponse reply);

    Task ReplyReactionChangedAsync(
        int clubId, int discussionId, int replyId, int likeCount, int dislikeCount);

    Task CommentCreatedAsync(int clubId, int postId, PostCommentResponse comment);

    Task CommentUpdatedAsync(int clubId, int postId, PostCommentResponse comment);

    Task CommentDeletedAsync(int clubId, int postId, PostCommentResponse comment);

    Task CommentReactionChangedAsync(
        int clubId, int postId, int commentId, int likeCount, int dislikeCount);
}
