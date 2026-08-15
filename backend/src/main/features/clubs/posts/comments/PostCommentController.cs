using System.Text.Json;
using System.Threading.Channels;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.clubs.posts.comments.contracts.requests;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.posts.comments;

/// <summary>Nested comments, reactions, and live updates for club posts.</summary>
[ApiController]
[FeatureGate(FeatureFlagKeys.ClubsPosts)]
[Route("clubs")]
public sealed class PostCommentController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPostCommentService _commentService;
    private readonly CommentEventBroker _broker;

    public PostCommentController(IPostCommentService commentService, CommentEventBroker broker)
    {
        _commentService = commentService;
        _broker = broker;
    }

    [AllowAnonymous]
    [HttpGet("{clubId}/posts/{postId}/comments")]
    [ProducesResponseType(typeof(ApiResponse<PostCommentPageResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetComments(
        int clubId,
        int postId,
        [FromQuery] int? parentCommentId = null,
        [FromQuery] PostCommentSort sort = PostCommentSort.Newest,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        var (userId, userRole) = GetOptionalUser();
        pageSize = Math.Clamp(pageSize < 1 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var page = await _commentService.GetPageAsync(
            clubId, postId, parentCommentId, sort, cursor, pageSize, userId, userRole);
        var response = new PostCommentPageResponse
        {
            Items = page.Items.Select(item => MapComment(item)),
            TotalCount = page.TotalCount,
            NextCursor = page.NextCursor,
            HasMore = page.HasMore
        };
        return Ok(new ApiResponse<PostCommentPageResponse>("Post comments fetched successfully.", response));
    }

    [Authorize]
    [HttpPost("{clubId}/posts/{postId}/comments")]
    [ProducesResponseType(typeof(ApiResponse<PostCommentResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateComment(
        int clubId, int postId, [FromBody] PostCommentCreateRequest request)
    {
        var user = User.GetUserPayload();
        var view = await _commentService.CreateAsync(
            clubId, postId, request.ParentCommentId, user.Id, user.Role, request.Content);
        var response = MapComment(view);
        _broker.Publish(postId, new CommentEvent("CommentCreated", response));
        return StatusCode(201, new ApiResponse<PostCommentResponse>("Comment created successfully.", response));
    }

    [Authorize]
    [HttpPut("{clubId}/posts/{postId}/comments/{commentId}")]
    [ProducesResponseType(typeof(ApiResponse<PostCommentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateComment(
        int clubId, int postId, int commentId, [FromBody] PostCommentUpdateRequest request)
    {
        var user = User.GetUserPayload();
        var view = await _commentService.UpdateAsync(
            clubId, postId, commentId, user.Id, user.Role, request.Content);
        var response = MapComment(view);
        _broker.Publish(postId, new CommentEvent("CommentUpdated", MapComment(view, false)));
        return Ok(new ApiResponse<PostCommentResponse>("Comment updated successfully.", response));
    }

    [Authorize]
    [HttpDelete("{clubId}/posts/{postId}/comments/{commentId}")]
    [ProducesResponseType(typeof(ApiResponse<PostCommentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteComment(int clubId, int postId, int commentId)
    {
        var user = User.GetUserPayload();
        var view = await _commentService.DeleteAsync(clubId, postId, commentId, user.Id, user.Role);
        var response = MapComment(view);
        _broker.Publish(postId, new CommentEvent("CommentDeleted", response));
        return Ok(new ApiResponse<PostCommentResponse>("Comment deleted successfully.", response));
    }

    [Authorize]
    [HttpPut("{clubId}/posts/{postId}/comments/{commentId}/reaction")]
    [ProducesResponseType(typeof(ApiResponse<PostCommentReactionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetReaction(
        int clubId, int postId, int commentId, [FromBody] PostCommentReactionRequest request)
    {
        var user = User.GetUserPayload();
        var summary = await _commentService.SetReactionAsync(
            clubId, postId, commentId, user.Id, user.Role, request.Reaction!.Value);
        return PublishReaction(postId, commentId, summary);
    }

    [Authorize]
    [HttpDelete("{clubId}/posts/{postId}/comments/{commentId}/reaction")]
    [ProducesResponseType(typeof(ApiResponse<PostCommentReactionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearReaction(int clubId, int postId, int commentId)
    {
        var user = User.GetUserPayload();
        var summary = await _commentService.ClearReactionAsync(
            clubId, postId, commentId, user.Id, user.Role);
        return PublishReaction(postId, commentId, summary);
    }

    [AllowAnonymous]
    [DisableRequestTimeout]
    [HttpGet("{clubId}/posts/{postId}/comments/events")]
    public async Task StreamComments(int clubId, int postId, CancellationToken cancellationToken)
    {
        var (userId, userRole) = GetOptionalUser();
        await _commentService.EnsureCanReadPostAsync(clubId, postId, userId, userRole);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<CommentEvent>();
        _broker.Subscribe(postId, subscriptionId, channel.Writer);
        try
        {
            await Response.WriteAsync(": keepalive\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var available = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                if (await Task.WhenAny(available, heartbeat) == heartbeat)
                {
                    await Response.WriteAsync(": keepalive\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    continue;
                }

                if (!await available)
                    break;
                while (channel.Reader.TryRead(out var evt))
                {
                    var json = JsonSerializer.Serialize(evt.Payload, EventJsonOptions);
                    await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", cancellationToken);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A disconnected browser or proxy is the normal end of an SSE subscription.
        }
        finally
        {
            _broker.Unsubscribe(postId, subscriptionId);
            channel.Writer.TryComplete();
        }
    }

    private IActionResult PublishReaction(
        int postId, int commentId, PostCommentReactionSummary summary)
    {
        var response = new PostCommentReactionResponse
        {
            CommentId = commentId,
            LikeCount = summary.LikeCount,
            DislikeCount = summary.DislikeCount,
            CurrentUserReaction = summary.CurrentUserReaction?.ToString()
        };
        _broker.Publish(postId, new CommentEvent("CommentReactionChanged", new
        {
            postId,
            commentId,
            likeCount = summary.LikeCount,
            dislikeCount = summary.DislikeCount
        }));
        return Ok(new ApiResponse<PostCommentReactionResponse>("Comment reaction updated successfully.", response));
    }

    private (int? UserId, string? UserRole) GetOptionalUser()
    {
        if (User.Identity?.IsAuthenticated != true)
            return (null, null);
        var user = User.GetUserPayload();
        return (user.Id, user.Role);
    }

    private static PostCommentResponse MapComment(
        PostCommentView view, bool includeCurrentUserReaction = true)
    {
        var comment = view.Comment;
        return new PostCommentResponse(
            comment.Id,
            comment.PostId,
            comment.ParentCommentId,
            comment.IsDeleted ? null : comment.UserId,
            comment.IsDeleted ? null : comment.Content,
            comment.CreatedAt,
            comment.UpdatedAt)
        {
            Author = comment.IsDeleted ? null : new AuthorInfo
            {
                Id = comment.UserId,
                Name = view.Author?.Name,
                Username = view.Author?.Username,
                Avatar = view.Author?.Avatar
            },
            IsDeleted = comment.IsDeleted,
            LikeCount = view.LikeCount,
            DislikeCount = view.DislikeCount,
            CurrentUserReaction = includeCurrentUserReaction
                ? view.CurrentUserReaction?.ToString()
                : null,
            DirectReplyCount = view.DirectReplyCount
        };
    }
}
