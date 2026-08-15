using System.Text.Json;
using System.Threading.Channels;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.clubs.discussions.replies.contracts.requests;
using backend.main.features.clubs.discussions.replies.contracts.responses;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.discussions.replies;

/// <summary>Nested replies, reactions, and live updates for club discussions.</summary>
[ApiController]
[FeatureGate(FeatureFlagKeys.ClubsDiscussions)]
[Route("clubs")]
public sealed class ClubDiscussionReplyController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IClubDiscussionReplyService _replyService;
    private readonly DiscussionReplyEventBroker _broker;

    public ClubDiscussionReplyController(
        IClubDiscussionReplyService replyService,
        DiscussionReplyEventBroker broker)
    {
        _replyService = replyService;
        _broker = broker;
    }

    [AllowAnonymous]
    [HttpGet("{clubId}/discussions/{discussionId}/replies")]
    [ProducesResponseType(typeof(ApiResponse<DiscussionReplyPageResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReplies(
        int clubId,
        int discussionId,
        [FromQuery] int? parentReplyId = null,
        [FromQuery] DiscussionReplySort sort = DiscussionReplySort.Newest,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        var (userId, userRole) = GetOptionalUser();
        pageSize = Math.Clamp(pageSize < 1 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var page = await _replyService.GetPageAsync(
            clubId, discussionId, parentReplyId, sort, cursor, pageSize, userId, userRole);
        var response = new DiscussionReplyPageResponse
        {
            Items = page.Items.Select(item => MapReply(item)),
            TotalCount = page.TotalCount,
            NextCursor = page.NextCursor,
            HasMore = page.HasMore
        };
        return Ok(new ApiResponse<DiscussionReplyPageResponse>("Discussion replies fetched successfully.", response));
    }

    [Authorize]
    [HttpPost("{clubId}/discussions/{discussionId}/replies")]
    [ProducesResponseType(typeof(ApiResponse<DiscussionReplyResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReply(
        int clubId, int discussionId, [FromBody] DiscussionReplyCreateRequest request)
    {
        var user = User.GetUserPayload();
        var view = await _replyService.CreateAsync(
            clubId, discussionId, request.ParentReplyId, user.Id, user.Role, request.Content);
        var response = MapReply(view);
        _broker.Publish(clubId, new DiscussionReplyEvent("ReplyCreated", response));
        return StatusCode(201, new ApiResponse<DiscussionReplyResponse>("Reply created successfully.", response));
    }

    [Authorize]
    [HttpPut("{clubId}/discussions/{discussionId}/replies/{replyId}")]
    [ProducesResponseType(typeof(ApiResponse<DiscussionReplyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateReply(
        int clubId, int discussionId, int replyId, [FromBody] DiscussionReplyUpdateRequest request)
    {
        var user = User.GetUserPayload();
        var view = await _replyService.UpdateAsync(
            clubId, discussionId, replyId, user.Id, user.Role, request.Content);
        var response = MapReply(view);
        _broker.Publish(clubId, new DiscussionReplyEvent("ReplyUpdated", MapReply(view, false)));
        return Ok(new ApiResponse<DiscussionReplyResponse>("Reply updated successfully.", response));
    }

    [Authorize]
    [HttpDelete("{clubId}/discussions/{discussionId}/replies/{replyId}")]
    [ProducesResponseType(typeof(ApiResponse<DiscussionReplyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteReply(int clubId, int discussionId, int replyId)
    {
        var user = User.GetUserPayload();
        var view = await _replyService.DeleteAsync(clubId, discussionId, replyId, user.Id, user.Role);
        var response = MapReply(view);
        _broker.Publish(clubId, new DiscussionReplyEvent("ReplyDeleted", response));
        return Ok(new ApiResponse<DiscussionReplyResponse>("Reply deleted successfully.", response));
    }

    [Authorize]
    [HttpPut("{clubId}/discussions/{discussionId}/replies/{replyId}/reaction")]
    [ProducesResponseType(typeof(ApiResponse<DiscussionReplyReactionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetReaction(
        int clubId, int discussionId, int replyId, [FromBody] DiscussionReplyReactionRequest request)
    {
        var user = User.GetUserPayload();
        var summary = await _replyService.SetReactionAsync(
            clubId, discussionId, replyId, user.Id, user.Role, request.Reaction!.Value);
        return PublishReaction(clubId, discussionId, replyId, summary);
    }

    [Authorize]
    [HttpDelete("{clubId}/discussions/{discussionId}/replies/{replyId}/reaction")]
    [ProducesResponseType(typeof(ApiResponse<DiscussionReplyReactionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearReaction(int clubId, int discussionId, int replyId)
    {
        var user = User.GetUserPayload();
        var summary = await _replyService.ClearReactionAsync(
            clubId, discussionId, replyId, user.Id, user.Role);
        return PublishReaction(clubId, discussionId, replyId, summary);
    }

    [AllowAnonymous]
    [HttpGet("{clubId}/discussions/replies/events")]
    public async Task StreamReplyEvents(int clubId, CancellationToken cancellationToken)
    {
        var (userId, userRole) = GetOptionalUser();
        await _replyService.EnsureCanReadClubAsync(clubId, userId, userRole);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<DiscussionReplyEvent>();
        _broker.Subscribe(clubId, subscriptionId, channel.Writer);
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

                if (!await available) break;
                while (channel.Reader.TryRead(out var evt))
                {
                    var json = JsonSerializer.Serialize(evt.Payload, EventJsonOptions);
                    await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", cancellationToken);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            _broker.Unsubscribe(clubId, subscriptionId);
            channel.Writer.TryComplete();
        }
    }

    private IActionResult PublishReaction(
        int clubId, int discussionId, int replyId, DiscussionReplyReactionSummary summary)
    {
        var response = new DiscussionReplyReactionResponse
        {
            ReplyId = replyId,
            LikeCount = summary.LikeCount,
            DislikeCount = summary.DislikeCount,
            CurrentUserReaction = summary.CurrentUserReaction?.ToString()
        };
        _broker.Publish(clubId, new DiscussionReplyEvent("ReplyReactionChanged", new
        {
            discussionId,
            replyId,
            likeCount = summary.LikeCount,
            dislikeCount = summary.DislikeCount
        }));
        return Ok(new ApiResponse<DiscussionReplyReactionResponse>("Reply reaction updated successfully.", response));
    }

    private (int? UserId, string? UserRole) GetOptionalUser()
    {
        if (User.Identity?.IsAuthenticated != true) return (null, null);
        var user = User.GetUserPayload();
        return (user.Id, user.Role);
    }

    private static DiscussionReplyResponse MapReply(
        DiscussionReplyView view, bool includeCurrentUserReaction = true)
    {
        var reply = view.Reply;
        return new DiscussionReplyResponse
        {
            Id = reply.Id,
            DiscussionId = reply.DiscussionId,
            ParentReplyId = reply.ParentReplyId,
            UserId = reply.IsDeleted ? null : reply.UserId,
            Content = reply.IsDeleted ? null : reply.Content,
            Author = reply.IsDeleted ? null : new AuthorInfo
            {
                Id = reply.UserId,
                Name = view.Author?.Name,
                Username = view.Author?.Username,
                Avatar = view.Author?.Avatar
            },
            IsDeleted = reply.IsDeleted,
            CreatedAt = reply.CreatedAt,
            UpdatedAt = reply.UpdatedAt,
            LikeCount = view.LikeCount,
            DislikeCount = view.DislikeCount,
            CurrentUserReaction = includeCurrentUserReaction
                ? view.CurrentUserReaction?.ToString()
                : null,
            DirectReplyCount = view.DirectReplyCount
        };
    }
}
