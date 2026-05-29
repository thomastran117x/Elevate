using System.Text.Json;
using System.Threading.Channels;

using backend.main.application.security;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.comments.contracts.requests;
using backend.main.features.clubs.posts.comments.contracts.responses;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.posts.comments
{
    /// <summary>
    /// Comment creation, moderation, and live-stream endpoints for club posts.
    /// </summary>
    [ApiController]
    [Route("clubs")]
    public class PostCommentController : ControllerBase
    {
        private readonly IPostCommentService _commentService;
        private readonly IUserRepository _userRepository;
        private readonly CommentEventBroker _broker;

        public PostCommentController(
            IPostCommentService commentService,
            IUserRepository userRepository,
            CommentEventBroker broker)
        {
            _commentService = commentService;
            _userRepository = userRepository;
            _broker = broker;
        }

        [Authorize]
        [HttpPost("{clubId}/posts/{postId}/comments")]
        [ProducesResponseType(typeof(ApiResponse<PostCommentResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateComment(
            int clubId,
            int postId,
            [FromBody] PostCommentCreateRequest request)
        {
            var userPayload = User.GetUserPayload();

            PostComment comment = await _commentService.CreateAsync(
                clubId, postId, userPayload.Id, request.Content);

            var author = await GetAuthorAsync(userPayload.Id);
            var response = MapToResponse(comment, author);

            _broker.Publish(postId, new CommentEvent("CommentCreated", response));

            return StatusCode(201, new ApiResponse<PostCommentResponse>(
                $"Comment on post with ID {postId} has been created successfully.",
                response
            ));
        }

        [AllowAnonymous]
        [HttpGet("{clubId}/posts/{postId}/comments")]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<PostCommentResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetComments(
            int clubId,
            int postId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (items, totalCount, authors) = await _commentService.GetByPostIdAsync(
                clubId, postId, page, pageSize);

            var paged = new PagedResponse<PostCommentResponse>(
                items.Select(c => MapToResponse(c, authors.GetValueOrDefault(c.UserId))),
                totalCount,
                page,
                pageSize
            );

            return StatusCode(200, new ApiResponse<PagedResponse<PostCommentResponse>>(
                $"Comments for post with ID {postId} have been fetched successfully.",
                paged
            ));
        }

        [AllowAnonymous]
        [HttpGet("{clubId}/posts/{postId}/comments/events")]
        public async Task StreamComments(int clubId, int postId, CancellationToken cancellationToken)
        {
            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            var id = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<CommentEvent>();
            _broker.Subscribe(postId, id, channel.Writer);

            try
            {
                await Response.WriteAsync(": keepalive\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    var json = JsonSerializer.Serialize(evt.Payload);
                    await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            finally
            {
                _broker.Unsubscribe(postId, id);
                channel.Writer.Complete();
            }
        }

        [Authorize]
        [HttpPut("{clubId}/posts/{postId}/comments/{commentId}")]
        [ProducesResponseType(typeof(ApiResponse<PostCommentResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateComment(
            int clubId,
            int postId,
            int commentId,
            [FromBody] PostCommentUpdateRequest request)
        {
            var userPayload = User.GetUserPayload();

            PostComment comment = await _commentService.UpdateAsync(
                postId, commentId, userPayload.Id, request.Content);

            var author = await GetAuthorAsync(userPayload.Id);
            var response = MapToResponse(comment, author);

            _broker.Publish(postId, new CommentEvent("CommentUpdated", response));

            return StatusCode(200, new ApiResponse<PostCommentResponse>(
                $"Comment with ID {commentId} has been updated successfully.",
                response
            ));
        }

        [Authorize]
        [HttpDelete("{clubId}/posts/{postId}/comments/{commentId}")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteComment(int clubId, int postId, int commentId)
        {
            var userPayload = User.GetUserPayload();

            await _commentService.DeleteAsync(postId, commentId, userPayload.Id);

            _broker.Publish(postId, new CommentEvent("CommentDeleted", new { postId, commentId }));

            return StatusCode(200, new MessageResponse(
                $"Comment with ID {commentId} has been deleted successfully."
            ));
        }

        private async Task<UserListRecord?> GetAuthorAsync(int userId)
        {
            var users = await _userRepository.GetByIdsAsync([userId]);
            return users.FirstOrDefault();
        }

        private static PostCommentResponse MapToResponse(PostComment c, UserListRecord? user = null) =>
            new(c.Id, c.PostId, c.UserId, c.Content, c.CreatedAt, c.UpdatedAt)
            {
                Author = new AuthorInfo { Id = c.UserId, Name = user?.Name, Username = user?.Username, Avatar = user?.Avatar }
            };
    }
}
