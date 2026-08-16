using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.features.clubs.posts.comments.contracts.responses;

using Microsoft.AspNetCore.SignalR;

namespace backend.main.features.clubs.realtime;

/// <inheritdoc cref="IClubRealtimeNotifier"/>
public sealed class ClubRealtimeNotifier : IClubRealtimeNotifier
{
    private readonly IHubContext<ClubRealtimeHub> _hub;

    public ClubRealtimeNotifier(IHubContext<ClubRealtimeHub> hub)
    {
        _hub = hub;
    }

    public Task ReplyCreatedAsync(int clubId, DiscussionReplyResponse reply) =>
        ToClub(clubId, ClubRealtimeEvents.ReplyCreated, reply);

    public Task ReplyUpdatedAsync(int clubId, DiscussionReplyResponse reply) =>
        ToClub(clubId, ClubRealtimeEvents.ReplyUpdated, reply);

    public Task ReplyDeletedAsync(int clubId, DiscussionReplyResponse reply) =>
        ToClub(clubId, ClubRealtimeEvents.ReplyDeleted, reply);

    public Task ReplyReactionChangedAsync(
        int clubId, int discussionId, int replyId, int likeCount, int dislikeCount) =>
        ToClub(clubId, ClubRealtimeEvents.ReplyReactionChanged, new ReplyReactionChangedPayload(
            discussionId, replyId, likeCount, dislikeCount));

    public Task CommentCreatedAsync(int clubId, int postId, PostCommentResponse comment) =>
        ToPost(clubId, postId, ClubRealtimeEvents.CommentCreated, comment);

    public Task CommentUpdatedAsync(int clubId, int postId, PostCommentResponse comment) =>
        ToPost(clubId, postId, ClubRealtimeEvents.CommentUpdated, comment);

    public Task CommentDeletedAsync(int clubId, int postId, PostCommentResponse comment) =>
        ToPost(clubId, postId, ClubRealtimeEvents.CommentDeleted, comment);

    public Task CommentReactionChangedAsync(
        int clubId, int postId, int commentId, int likeCount, int dislikeCount) =>
        ToPost(clubId, postId, ClubRealtimeEvents.CommentReactionChanged, new CommentReactionChangedPayload(
            postId, commentId, likeCount, dislikeCount));

    private Task ToClub(int clubId, string eventName, object payload) =>
        _hub.Clients.Group(ClubRealtimeGroups.Club(clubId)).SendAsync(eventName, payload);

    private Task ToPost(int clubId, int postId, string eventName, object payload) =>
        _hub.Clients.Group(ClubRealtimeGroups.Post(clubId, postId)).SendAsync(eventName, payload);
}

/// <summary>Reaction counts only; the actor's own reaction is deliberately not broadcast.</summary>
public sealed record ReplyReactionChangedPayload(
    int DiscussionId, int ReplyId, int LikeCount, int DislikeCount);

/// <inheritdoc cref="ReplyReactionChangedPayload"/>
public sealed record CommentReactionChangedPayload(
    int PostId, int CommentId, int LikeCount, int DislikeCount);
