using backend.main.application.security;
using backend.main.dtos.requests.club;
using backend.main.dtos.responses.club;
using backend.main.dtos.responses.general;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("clubs")]
    public class ClubController : ControllerBase
    {
        private readonly IClubService _clubService;

        public ClubController(IClubService clubService)
        {
            _clubService = clubService;
        }


        [Authorize]
        [HttpPost("{clubId}/join")]
        public async Task<IActionResult> JoinClub(int clubId)
        {
            var userPayload = User.GetUserPayload();

            await _clubService.JoinClubAsync(clubId, userPayload.Id);

            return StatusCode(
                200,
                new MessageResponse(
                    $"The club with ID `{clubId}` has been followed successfully."
                )
            );
        }

        [Authorize]
        [HttpDelete("{clubId}/join")]
        public async Task<IActionResult> LeaveClub(int clubId)
        {
            var userPayload = User.GetUserPayload();

            await _clubService.LeaveClubAsync(clubId, userPayload.Id);

            return StatusCode(
                200,
                new MessageResponse(
                    $"The club with ID `{clubId}` has been unfollowed successfully."
                )
            );
        }

        [Authorize]
        [HttpPost("")]
        public async Task<IActionResult> CreateClub([FromForm] ClubCreateRequest request)
        {
            var userPayload = User.GetUserPayload();

            Club club = await _clubService.CreateClub(
                name: request.Name,
                userId: userPayload.Id,
                description: request.Description,
                clubtype: request.Clubtype,
                clubimage: request.ClubImage,
                phone: request.Phone,
                email: request.Email
            );

            ClubResponse response = MapToResponse(club);

            return StatusCode(
                201,
                new ApiResponse<ClubResponse>(
                    $"The club with ID {club.Id} has been created successfully.",
                    response
                )
            );
        }

        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateClub([FromForm] ClubUpdateRequest request, int id)
        {
            var userPayload = User.GetUserPayload();

            Club club = await _clubService.UpdateClub(
                clubId: id,
                userId: userPayload.Id,
                name: request.Name,
                description: request.Description,
                clubtype: request.Clubtype,
                clubimage: request.ClubImage,
                phone: request.Phone,
                email: request.Email
            );

            ClubResponse response = MapToResponse(club);

            return StatusCode(
                200,
                new ApiResponse<ClubResponse>(
                    $"The club with ID {id} has been updated successfully.",
                    response
                )
            );
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteClub(int id)
        {
            var userPayload = User.GetUserPayload();

            await _clubService.DeleteClub(id, userPayload.Id);

            return StatusCode(
                200,
                new MessageResponse(
                    $"The club with ID {id} has been deleted successfully."
                )
            );
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetClub(int id)
        {
            Club club = await _clubService.GetClub(id);

            ClubResponse response = MapToResponse(club);

            return StatusCode(
                200,
                new ApiResponse<ClubResponse>(
                    $"The club with ID {id} has been fetched successfully.",
                    response
                )
            );
        }

        [HttpGet("")]
        public async Task<IActionResult> GetClubs(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            List<Club> clubs = await _clubService
                .GetAllClubs(search, page, pageSize);

            IEnumerable<ClubResponse> responses = clubs.Select(MapToResponse);

            return StatusCode(
                200,
                new ApiResponse<IEnumerable<ClubResponse>>(
                    $"The clubs have been fetched successfully.",
                    responses
                )
            );
        }

        private static ClubResponse MapToResponse(Club club)
        {
            return new ClubResponse(
                club.Id,
                club.UserId,
                club.Name,
                club.Description,
                club.Clubtype.ToString(),
                club.ClubImage,
                club.MemberCount,
                club.EventCount,
                club.AvaliableEventCount,
                club.MaxMemberCount,
                club.isPrivate
            )
            {
                Phone = club.Phone,
                Email = club.Email,
                Rating = club.Rating
            };
        }
    }
}
