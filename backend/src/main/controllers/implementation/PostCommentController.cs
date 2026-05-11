using backend.main.application.security;
using backend.main.dtos.requests.postcomment;
using backend.main.dtos.responses.general;
using backend.main.dtos.responses.postcomment;
using backend.main.models.core;
using backend.main.services.interfaces;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("clubs")]
    public class PostCommentController : ControllerBase
    {
        private readonly IPostCommentService _commentService;

        public PostCommentController(IPostCommentService commentService)
        {
            _commentService = commentService;
        }

        [Authorize]
        [HttpPost("{clubId}/posts/{postId}/comments")]
        public async Task<IActionResult> CreateComment(
            int clubId,
            int postId,
            [FromBody] PostCommentCreateRequest request)
        {
            var userPayload = User.GetUserPayload();

            PostComment comment = await _commentService.CreateAsync(
                clubId, postId, userPayload.Id, request.Content);

            return StatusCode(
                201,
                new ApiResponse<PostCommentResponse>(
                    $"Comment on post with ID {postId} has been created successfully.",
                    MapToResponse(comment)
                )
            );
        }

        [AllowAnonymous]
        [HttpGet("{clubId}/posts/{postId}/comments")]
        public async Task<IActionResult> GetComments(
            int clubId,
            int postId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (items, totalCount) = await _commentService.GetByPostIdAsync(
                clubId, postId, page, pageSize);

            var paged = new PagedResponse<PostCommentResponse>(
                items.Select(MapToResponse),
                totalCount,
                page,
                pageSize
            );

            return StatusCode(
                200,
                new ApiResponse<PagedResponse<PostCommentResponse>>(
                    $"Comments for post with ID {postId} have been fetched successfully.",
                    paged
                )
            );
        }

        [Authorize]
        [HttpPut("{clubId}/posts/{postId}/comments/{commentId}")]
        public async Task<IActionResult> UpdateComment(
            int clubId,
            int postId,
            int commentId,
            [FromBody] PostCommentUpdateRequest request)
        {
            var userPayload = User.GetUserPayload();

            PostComment comment = await _commentService.UpdateAsync(
                postId, commentId, userPayload.Id, request.Content);

            return StatusCode(
                200,
                new ApiResponse<PostCommentResponse>(
                    $"Comment with ID {commentId} has been updated successfully.",
                    MapToResponse(comment)
                )
            );
        }

        [Authorize]
        [HttpDelete("{clubId}/posts/{postId}/comments/{commentId}")]
        public async Task<IActionResult> DeleteComment(int clubId, int postId, int commentId)
        {
            var userPayload = User.GetUserPayload();

            await _commentService.DeleteAsync(postId, commentId, userPayload.Id);

            return StatusCode(
                200,
                new MessageResponse(
                    $"Comment with ID {commentId} has been deleted successfully."
                )
            );
        }

        private static PostCommentResponse MapToResponse(PostComment c) =>
            new(c.Id, c.PostId, c.UserId, c.Content, c.CreatedAt, c.UpdatedAt);
    }
}
