using backend.main.application.security;
using backend.main.application.security.authorization;
using backend.main.features.clubs.moderation.contracts.responses;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.moderation;

/// <summary>A club's moderation log: who changed or removed someone else's content, and why.</summary>
[ApiController]
[Route("clubs")]
public sealed class ClubModerationController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IClubModerationLog _moderationLog;
    private readonly IUserRepository _userRepository;

    public ClubModerationController(IClubModerationLog moderationLog, IUserRepository userRepository)
    {
        _moderationLog = moderationLog;
        _userRepository = userRepository;
    }

    [Authorize]
    [HttpGet("{clubId}/moderation-log")]
    [RequirePermission(PermissionNames.ClubManage, "clubId", DenialMessage = ClubModerationLog.ReadForbiddenMessage)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<ClubModerationLogItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModerationLog(
        int clubId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? DefaultPageSize : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        var userPayload = User.GetUserPayload();
        var (items, totalCount) = await _moderationLog.GetPageAsync(
            clubId, userPayload.Id, userPayload.Role, page, pageSize, cancellationToken);

        var userIds = items.SelectMany(item => new[] { item.ActorUserId, item.AuthorUserId }).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, UserListRecord>()
            : (await _userRepository.GetByIdsAsync(userIds)).ToDictionary(user => user.Id);

        var paged = new PagedResponse<ClubModerationLogItemResponse>(
            items.Select(item => new ClubModerationLogItemResponse(
                item.Id,
                item.ClubId,
                item.Action,
                item.ContentType,
                item.ContentId,
                item.ContentSnapshot,
                item.Reason,
                item.ActorClubRole,
                ToAuthor(item.ActorUserId, users.GetValueOrDefault(item.ActorUserId)),
                ToAuthor(item.AuthorUserId, users.GetValueOrDefault(item.AuthorUserId)),
                item.CreatedAt)),
            totalCount,
            page,
            pageSize);

        return Ok(new ApiResponse<PagedResponse<ClubModerationLogItemResponse>>(
            $"Moderation log for club with ID {clubId} has been fetched successfully.",
            paged));
    }

    private static AuthorInfo ToAuthor(int userId, UserListRecord? user) => new()
    {
        Id = userId,
        Name = user?.Name,
        Username = user?.Username,
        UsernameDisplay = user?.UsernameDisplay ?? user?.Username,
        Avatar = user?.Avatar,
    };
}
