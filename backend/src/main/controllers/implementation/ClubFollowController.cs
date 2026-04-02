using backend.main.configurations.security;
using backend.main.dtos.responses.follow;
using backend.main.dtos.responses.general;
using backend.main.models.core;
using backend.main.services.interfaces;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("clubs")]
    public class ClubFollowController : ControllerBase
    {
        private readonly IFollowService _followService;

        public ClubFollowController(IFollowService followService)
        {
            _followService = followService;
        }

        [HttpGet("{clubId}/members")]
        public async Task<IActionResult> GetClubMembers(
            int clubId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            IEnumerable<FollowClub> follows = await _followService.GetFollowsByClubAsync(clubId, page, pageSize);

            IEnumerable<FollowResponse> responses = follows.Select(MapToResponse);

            return StatusCode(
                200,
                new ApiResponse<IEnumerable<FollowResponse>>(
                    $"Members for club with ID {clubId} have been fetched successfully.",
                    responses
                )
            );
        }

        [Authorize]
        [HttpGet("{clubId}/members/me")]
        public async Task<IActionResult> CheckMembership(int clubId)
        {
            var userPayload = User.GetUserPayload();

            bool isMember = await _followService.IsMemberAsync(clubId, userPayload.Id);

            return StatusCode(
                200,
                new ApiResponse<object>(
                    $"Membership status for club with ID {clubId} has been fetched successfully.",
                    new { isMember }
                )
            );
        }

        private static FollowResponse MapToResponse(FollowClub follow)
        {
            return new FollowResponse(follow.Id, follow.UserId, follow.ClubId, follow.CreatedAt);
        }
    }

    [ApiController]
    [Route("users")]
    public class UserFollowController : ControllerBase
    {
        private readonly IFollowService _followService;

        public UserFollowController(IFollowService followService)
        {
            _followService = followService;
        }

        [Authorize]
        [HttpGet("{userId}/clubs/following")]
        public async Task<IActionResult> GetClubsFollowedByUser(
            int userId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            IEnumerable<FollowClub> follows = await _followService.GetFollowsByUserAsync(userId, page, pageSize);

            IEnumerable<FollowResponse> responses = follows.Select(f =>
                new FollowResponse(f.Id, f.UserId, f.ClubId, f.CreatedAt)
            );

            return StatusCode(
                200,
                new ApiResponse<IEnumerable<FollowResponse>>(
                    $"Clubs followed by user with ID {userId} have been fetched successfully.",
                    responses
                )
            );
        }
    }
}
