using backend.main.application.security;
using backend.main.features.clubs.contracts.requests;
using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.versions;
using backend.main.features.clubs.versions.contracts.responses;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs
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
                userRole: userPayload.Role,
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

        [Authorize]
        [HttpGet("{id}/versions")]
        public async Task<IActionResult> GetClubVersions(
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var effectivePage = page < 1 ? 1 : page;
            var effectivePageSize = pageSize switch
            {
                < 1 => 20,
                > 100 => 100,
                _ => pageSize
            };

            var userPayload = User.GetUserPayload();
            var (items, totalCount) = await _clubService.GetVersionHistoryAsync(
                id,
                userPayload.Id,
                userPayload.Role,
                effectivePage,
                effectivePageSize);

            var paged = new PagedResponse<ClubVersionListItemResponse>(
                items.Select(MapToVersionListItemResponse),
                totalCount,
                effectivePage,
                effectivePageSize);

            return Ok(new ApiResponse<PagedResponse<ClubVersionListItemResponse>>(
                $"Version history for club with ID {id} has been fetched successfully.",
                paged
            ));
        }

        [Authorize]
        [HttpGet("{id}/versions/{versionNumber}")]
        public async Task<IActionResult> GetClubVersion(int id, int versionNumber)
        {
            var userPayload = User.GetUserPayload();
            var version = await _clubService.GetVersionDetailAsync(
                id,
                versionNumber,
                userPayload.Id,
                userPayload.Role);

            return Ok(new ApiResponse<ClubVersionDetailResponse>(
                $"Version {versionNumber} for club with ID {id} has been fetched successfully.",
                MapToVersionDetailResponse(version)
            ));
        }

        [Authorize]
        [HttpPost("{id}/versions/{versionNumber}/rollback")]
        public async Task<IActionResult> RollbackClubVersion(int id, int versionNumber)
        {
            var userPayload = User.GetUserPayload();
            var result = await _clubService.RollbackToVersionAsync(
                id,
                versionNumber,
                userPayload.Id,
                userPayload.Role);

            var response = new ClubRollbackResponse(
                MapToResponse(result.Club),
                result.RestoredFromVersionNumber,
                result.NewVersionNumber);

            return Ok(new ApiResponse<ClubRollbackResponse>(
                $"Club with ID {id} has been rolled back to version {versionNumber} successfully.",
                response
            ));
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
                club.isPrivate,
                club.CurrentVersionNumber
            )
            {
                Phone = club.Phone,
                Email = club.Email,
                Rating = club.Rating
            };
        }

        private static ClubVersionListItemResponse MapToVersionListItemResponse(ClubVersionHistoryItem item) =>
            new(
                item.VersionNumber,
                item.ActionType,
                item.CreatedAt,
                item.ActorUserId,
                item.ActorRole,
                item.RollbackEligible,
                item.RollbackExpiresAt,
                item.RollbackSourceVersionNumber,
                item.ChangedFields.Select(MapToFieldChangeResponse).ToList()
            );

        private static ClubVersionDetailResponse MapToVersionDetailResponse(ClubVersionDetail detail) =>
            new(
                detail.VersionNumber,
                detail.ActionType,
                detail.CreatedAt,
                detail.ActorUserId,
                detail.ActorRole,
                detail.RollbackEligible,
                detail.RollbackExpiresAt,
                detail.RollbackSourceVersionNumber,
                detail.ChangedFields.Select(MapToFieldChangeResponse).ToList(),
                new ClubVersionSnapshotResponse(
                    detail.Snapshot.Name,
                    detail.Snapshot.Description,
                    detail.Snapshot.Clubtype,
                    detail.Snapshot.ClubImage,
                    detail.Snapshot.Phone,
                    detail.Snapshot.Email,
                    detail.Snapshot.WebsiteUrl,
                    detail.Snapshot.Location,
                    detail.Snapshot.MaxMemberCount,
                    detail.Snapshot.IsPrivate
                )
            );

        private static ClubVersionFieldChangeResponse MapToFieldChangeResponse(ClubVersionFieldChange change) =>
            new(change.Field, change.OldValue, change.NewValue);
    }
}
