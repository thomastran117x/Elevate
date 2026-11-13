using backend.main.application.security.authorization;
using backend.main.features.clubs.access;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.access;

/// <summary>
/// Decides event permissions. <c>event.view</c> is the visibility policy in
/// <see cref="IEventAccessChecker"/>; <c>event.manage</c> and <c>event.media</c> follow the
/// caller's role in the event's club.
/// </summary>
public sealed class EventPermissionEvaluator : IResourcePermissionEvaluator
{
    private readonly AppDatabaseContext _db;
    private readonly IEventAccessChecker _accessChecker;
    private readonly ClubRelationshipResolver _relationships;

    public EventPermissionEvaluator(
        AppDatabaseContext db,
        IEventAccessChecker accessChecker,
        ClubRelationshipResolver relationships)
    {
        _db = db;
        _accessChecker = accessChecker;
        _relationships = relationships;
    }

    public async Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var eventId = ((EventResource)resource).EventId;

        if (permission.Name == PermissionNames.EventView)
        {
            var ev = await _db.Events
                .AsNoTracking()
                .FirstOrDefaultAsync(entry => entry.Id == eventId, cancellationToken);
            if (ev is null)
                return PermissionDecision.NotFound;

            var visible = await _accessChecker.CanViewEventAsync(ev, actor.UserId, actor.Role);
            return PermissionDecision.Of(visible, actor.BaseRelationships());
        }

        var clubId = await _db.Events
            .AsNoTracking()
            .Where(entry => entry.Id == eventId)
            .Select(entry => (int?)entry.ClubId)
            .FirstOrDefaultAsync(cancellationToken);

        return await ClubScopedPermission.DecideAsync(_relationships, actor, permission, clubId, cancellationToken);
    }
}

/// <summary>Decides a permission on a resource that inherits its access from the club that owns it.</summary>
public static class ClubScopedPermission
{
    public static async Task<PermissionDecision> DecideAsync(
        ClubRelationshipResolver relationships,
        ResourceActor actor,
        Permission permission,
        int? clubId,
        CancellationToken cancellationToken)
    {
        if (clubId is not int id)
            return PermissionDecision.NotFound;

        var relationship = await relationships.ResolveAsync(
            actor, id, ClubRelationshipResolver.RelevantTo(permission), cancellationToken);

        // The owning club is required by the schema, so a missing one means the resource itself is gone.
        return relationship is null
            ? PermissionDecision.NotFound
            : PermissionDecision.Decide(permission, relationship.Held, resourceIsPublic: false);
    }
}

/// <summary>Decides <c>series.manage</c> from the caller's role in the series' club.</summary>
public sealed class EventSeriesPermissionEvaluator : IResourcePermissionEvaluator
{
    private readonly AppDatabaseContext _db;
    private readonly ClubRelationshipResolver _relationships;

    public EventSeriesPermissionEvaluator(AppDatabaseContext db, ClubRelationshipResolver relationships)
    {
        _db = db;
        _relationships = relationships;
    }

    public async Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var seriesId = ((EventSeriesResource)resource).SeriesId;
        var clubId = await _db.EventSeries
            .AsNoTracking()
            .Where(series => series.Id == seriesId)
            .Select(series => (int?)series.ClubId)
            .FirstOrDefaultAsync(cancellationToken);

        return await ClubScopedPermission.DecideAsync(_relationships, actor, permission, clubId, cancellationToken);
    }
}
