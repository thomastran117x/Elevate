using backend.main.application.security.authorization;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.moderation;

public sealed class ClubModerationLog : IClubModerationLog
{
    public const string ReadForbiddenMessage = "You are not allowed to view this club's moderation log.";

    private readonly AppDatabaseContext _db;
    private readonly IResourceAuthorizer _authorizer;

    public ClubModerationLog(AppDatabaseContext db, IResourceAuthorizer authorizer)
    {
        _db = db;
        _authorizer = authorizer;
    }

    public void Stage(ClubModerationAction entry) => _db.ClubModerationActions.Add(entry);

    public async Task<(IReadOnlyList<ClubModerationAction> Items, int TotalCount)> GetPageAsync(
        int clubId, int userId, string? userRole, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await _authorizer.EnsureAsync(
            ResourceActor.User(userId, userRole),
            Permissions.ClubManage,
            new ClubResource(clubId),
            new PermissionMessages(ReadForbiddenMessage),
            cancellationToken);

        var query = _db.ClubModerationActions
            .AsNoTracking()
            .Where(entry => entry.ClubId == clubId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
