using backend.main.application.security;
using backend.main.features.clubs.contracts.requests;
using backend.main.features.clubs.contracts.responses;
using backend.main.features.clubs.search;
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

            ClubResponse response = MapToResponse(club, new ClubAccessInfo
            {
                IsOwner = true,
                CanManage = true
            });

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

            var access = await _clubService.GetClubAccessAsync(id, userPayload.Id, userPayload.Role);
            ClubResponse response = MapToResponse(club, access);

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
            var access = await ResolveAccessAsync([club.Id]);
            ClubResponse response = MapToResponse(club, access[club.Id]);

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
            [FromQuery] ClubType? clubType,
            [FromQuery] ClubSortBy sortBy = ClubSortBy.Relevance,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var criteria = PublicClubSearchCriteriaFactory.FromQuery(
                search,
                clubType,
                sortBy,
                page,
                pageSize);
            var (clubs, totalCount, source) = await _clubService.GetAllClubs(criteria);

            var accessMap = await ResolveAccessAsync(clubs.Select(club => club.Id));
            IEnumerable<ClubResponse> responses = clubs.Select(club => MapToResponse(club, accessMap[club.Id]));
            var paged = new PagedResponse<ClubResponse>(responses, totalCount, criteria.Page, criteria.PageSize);

            return StatusCode(
                200,
                new ApiResponse<PagedResponse<ClubResponse>>(
                    $"The clubs have been fetched successfully.",
                    paged,
                    source
                )
            );
        }

        [HttpPost("search")]
        public async Task<IActionResult> SearchClubs([FromBody] ClubSearchRequest request)
        {
            var criteria = PublicClubSearchCriteriaFactory.FromRequest(request);
            var (clubs, totalCount, source) = await _clubService.GetAllClubs(criteria);

            var accessMap = await ResolveAccessAsync(clubs.Select(club => club.Id));
            var responses = clubs.Select(club => MapToResponse(club, accessMap[club.Id]));
            var paged = new PagedResponse<ClubResponse>(responses, totalCount, criteria.Page, criteria.PageSize);

            return Ok(new ApiResponse<PagedResponse<ClubResponse>>(
                "The clubs have been fetched successfully.",
                paged,
                source
            ));
        }

        [Authorize]
        [HttpGet("managed")]
        public async Task<IActionResult> GetManagedClubs()
        {
            var userPayload = User.GetUserPayload();
            var clubs = await _clubService.GetManagedClubsAsync(userPayload.Id);
            var accessMap = await _clubService.GetClubAccessMapAsync(
                clubs.Select(club => club.Id),
                userPayload.Id,
                userPayload.Role);

            return Ok(new ApiResponse<IEnumerable<ClubResponse>>(
                "Managed clubs have been fetched successfully.",
                clubs.Select(club => MapToResponse(club, accessMap[club.Id]))
            ));
        }

        [Authorize]
        [HttpGet("{id}/staff")]
        public async Task<IActionResult> GetClubStaff(int id)
        {
            var userPayload = User.GetUserPayload();
            var staff = await _clubService.GetStaffAsync(id, userPayload.Id, userPayload.Role);

            return Ok(new ApiResponse<IEnumerable<ClubStaffResponse>>(
                $"Staff for club with ID {id} has been fetched successfully.",
                staff.Select(MapToStaffResponse)
            ));
        }

        [Authorize]
        [HttpPost("{id}/staff/managers")]
        public async Task<IActionResult> AddManager(int id, [FromBody] ClubStaffCreateRequest request)
        {
            var userPayload = User.GetUserPayload();
            var staff = await _clubService.AddStaffAsync(
                id,
                request.UserId,
                backend.main.features.clubs.staff.ClubStaffRole.Manager,
                userPayload.Id,
                userPayload.Role);

            return StatusCode(201, new ApiResponse<ClubStaffResponse>(
                $"Manager has been added to club with ID {id} successfully.",
                MapToStaffResponse(staff)
            ));
        }

        [Authorize]
        [HttpPost("{id}/staff/volunteers")]
        public async Task<IActionResult> AddVolunteer(int id, [FromBody] ClubStaffCreateRequest request)
        {
            var userPayload = User.GetUserPayload();
            var staff = await _clubService.AddStaffAsync(
                id,
                request.UserId,
                backend.main.features.clubs.staff.ClubStaffRole.Volunteer,
                userPayload.Id,
                userPayload.Role);

            return StatusCode(201, new ApiResponse<ClubStaffResponse>(
                $"Volunteer has been added to club with ID {id} successfully.",
                MapToStaffResponse(staff)
            ));
        }

        [Authorize]
        [HttpDelete("{id}/staff/{userId}")]
        public async Task<IActionResult> RemoveStaff(int id, int userId)
        {
            var userPayload = User.GetUserPayload();
            await _clubService.RemoveStaffAsync(id, userId, userPayload.Id, userPayload.Role);

            return Ok(new MessageResponse(
                $"Staff member with user ID {userId} has been removed from club with ID {id} successfully."
            ));
        }

        [Authorize]
        [HttpPost("{id}/transfer-ownership")]
        public async Task<IActionResult> TransferOwnership(int id, [FromBody] ClubOwnershipTransferRequest request)
        {
            var userPayload = User.GetUserPayload();
            var club = await _clubService.TransferOwnershipAsync(
                id,
                request.NewOwnerUserId,
                userPayload.Id,
                userPayload.Role);

            var access = await _clubService.GetClubAccessAsync(id, userPayload.Id, userPayload.Role);
            return Ok(new ApiResponse<ClubResponse>(
                $"Ownership for club with ID {id} has been transferred successfully.",
                MapToResponse(club, access)
            ));
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

        private async Task<Dictionary<int, ClubAccessInfo>> ResolveAccessAsync(IEnumerable<int> clubIds)
        {
            if (User.Identity?.IsAuthenticated != true)
                return clubIds.Distinct().ToDictionary(id => id, _ => new ClubAccessInfo());

            var userPayload = User.GetUserPayload();
            return await _clubService.GetClubAccessMapAsync(clubIds, userPayload.Id, userPayload.Role);
        }

        private static ClubResponse MapToResponse(Club club, ClubAccessInfo? access = null)
        {
            var response = new ClubResponse(
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
                Rating = club.Rating,
                WebsiteUrl = club.WebsiteUrl,
                Location = club.Location,
                IsOwner = access?.IsOwner ?? false,
                IsManager = access?.IsManager ?? false,
                IsVolunteer = access?.IsVolunteer ?? false,
                CanManage = access?.CanManage ?? false
            };

            return response;
        }

        private static ClubStaffResponse MapToStaffResponse(backend.main.features.clubs.staff.ClubStaff staff) =>
            new(
                staff.Id,
                staff.ClubId,
                staff.UserId,
                staff.Role.ToString(),
                staff.GrantedByUserId,
                staff.CreatedAt,
                staff.UpdatedAt
            );

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

    [ApiController]
    [Route("admin/clubs")]
    [Authorize("AdminOnly")]
    public class AdminClubsController : ControllerBase
    {
        private readonly IClubReindexService _reindexService;

        public AdminClubsController(IClubReindexService reindexService)
        {
            _reindexService = reindexService;
        }

        [HttpPost("reindex")]
        public async Task<IActionResult> ReindexClubs(CancellationToken cancellationToken)
        {
            var count = await _reindexService.ReindexAllAsync(cancellationToken);
            return Ok(new ApiResponse<object>(
                "Clubs reindexed successfully.",
                new { indexed = count }
            ));
        }
    }
}
