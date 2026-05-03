using backend.main.configurations.security;
using backend.main.dtos.requests.clubpost;
using backend.main.dtos.responses.clubpost;
using backend.main.dtos.responses.general;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.services.interfaces;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("clubs")]
    public class ClubPostController : ControllerBase
    {
        private readonly IClubPostService _postService;

        public ClubPostController(IClubPostService postService)
        {
            _postService = postService;
        }

        [Authorize]
        [HttpPost("{clubId}/posts")]
        public async Task<IActionResult> CreatePost(int clubId, [FromBody] ClubPostCreateRequest request)
        {
            var userPayload = User.GetUserPayload();

            ClubPost post = await _postService.CreateAsync(
                clubId, userPayload.Id, request.Title, request.Content, request.PostType, request.IsPinned);

            return StatusCode(
                201,
                new ApiResponse<ClubPostResponse>(
                    $"Post for club with ID {clubId} has been created successfully.",
                    MapToResponse(post)
                )
            );
        }

        [AllowAnonymous]
        [HttpGet("{clubId}/posts")]
        public async Task<IActionResult> GetPosts(
            int clubId,
            [FromQuery] string? search,
            [FromQuery] PostSortBy sortBy = PostSortBy.Recent,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            int? userId = null;
            if (User.Identity?.IsAuthenticated == true)
                userId = User.GetUserPayload().Id;

            var (items, totalCount, source) = await _postService.GetByClubIdAsync(
                clubId, userId, search, sortBy, page, pageSize);

            var paged = new PagedResponse<ClubPostResponse>(
                items.Select(post => MapToResponse(post, source)),
                totalCount,
                page,
                pageSize
            );

            return StatusCode(
                200,
                new ApiResponse<PagedResponse<ClubPostResponse>>(
                    $"Posts for club with ID {clubId} have been fetched successfully.",
                    paged
                )
            );
        }

        [Authorize]
        [HttpPut("{clubId}/posts/{id}")]
        public async Task<IActionResult> UpdatePost(
            int clubId,
            int id,
            [FromBody] ClubPostUpdateRequest request)
        {
            var userPayload = User.GetUserPayload();

            ClubPost post = await _postService.UpdateAsync(
                clubId, id, userPayload.Id, request.Title, request.Content, request.PostType, request.IsPinned);

            return StatusCode(
                200,
                new ApiResponse<ClubPostResponse>(
                    $"Post with ID {id} has been updated successfully.",
                    MapToResponse(post)
                )
            );
        }

        [Authorize]
        [HttpDelete("{clubId}/posts/{id}")]
        public async Task<IActionResult> DeletePost(int clubId, int id)
        {
            var userPayload = User.GetUserPayload();

            await _postService.DeleteAsync(clubId, id, userPayload.Id);

            return StatusCode(
                200,
                new MessageResponse(
                    $"Post with ID {id} has been deleted successfully."
                )
            );
        }

        private static ClubPostResponse MapToResponse(
            ClubPost p,
            string source = ResponseSource.Database) =>
            new(p.Id, p.ClubId, p.UserId, p.Title, p.Content, p.PostType, p.LikesCount, p.ViewCount, p.IsPinned, p.CreatedAt, p.UpdatedAt, source);
    }

    [ApiController]
    [Route("admin/clubs")]
    [Authorize("AdminOnly")]
    public class AdminClubPostController : ControllerBase
    {
        private readonly IClubPostService _postService;
        private readonly IClubPostReindexService _reindexService;

        public AdminClubPostController(IClubPostService postService, IClubPostReindexService reindexService)
        {
            _postService = postService;
            _reindexService = reindexService;
        }

        [HttpPost("posts/reindex")]
        public async Task<IActionResult> ReindexPosts(CancellationToken cancellationToken)
        {
            var count = await _reindexService.ReindexAllAsync(cancellationToken);
            return Ok(new { indexed = count });
        }

        [HttpGet("posts")]
        public async Task<IActionResult> GetAllPosts(
            [FromQuery] string? search,
            [FromQuery] PostSortBy sortBy = PostSortBy.Recent,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var (items, totalCount, source) = await _postService.GetAllAdminAsync(search, sortBy, page, pageSize);

            var paged = new PagedResponse<ClubPostResponse>(
                items.Select(post => MapToResponse(post, source)),
                totalCount,
                page,
                pageSize
            );

            return StatusCode(
                200,
                new ApiResponse<PagedResponse<ClubPostResponse>>(
                    "All posts have been fetched successfully.",
                    paged
                )
            );
        }

        private static ClubPostResponse MapToResponse(
            ClubPost p,
            string source = ResponseSource.Database) =>
            new(p.Id, p.ClubId, p.UserId, p.Title, p.Content, p.PostType, p.LikesCount, p.ViewCount, p.IsPinned, p.CreatedAt, p.UpdatedAt, source);
    }
}
