using backend.main.application.security.authorization;
using backend.main.features.clubs.staff;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

using static backend.main.application.security.authorization.ResourceRelationship;

namespace backend.main.features.clubs.access;

/// <summary>A caller's relationship to one club, as stored in the database.</summary>
/// <param name="ClubId">The club.</param>
/// <param name="IsPrivate">Whether the club is private.</param>
/// <param name="Held">The relationships the caller was found to hold.</param>
public sealed record ClubRelationship(int ClubId, bool IsPrivate, ResourceRelationship Held);

/// <summary>
/// Resolves a caller's club relationships from the database: ownership from <c>Club.UserId</c>,
/// staff roles from <c>ClubStaff</c>, membership from <c>FollowClub</c>. Nothing is read from the
/// club or membership caches, so a removed manager or member is refused on their next request.
/// </summary>
public sealed class ClubRelationshipResolver
{
    private readonly AppDatabaseContext _db;

    public ClubRelationshipResolver(AppDatabaseContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns <c>null</c> when the club does not exist. Only the relationships in
    /// <paramref name="relevant"/> are looked up, and the lookups stop once one of them is held.
    /// </summary>
    public async Task<ClubRelationship?> ResolveAsync(
        ResourceActor actor,
        int clubId,
        Func<bool, ResourceRelationship> relevant,
        CancellationToken cancellationToken = default)
    {
        var club = await _db.Clubs
            .AsNoTracking()
            .Where(entry => entry.Id == clubId)
            .Select(entry => new { entry.UserId, entry.isPrivate })
            .FirstOrDefaultAsync(cancellationToken);
        if (club is null)
            return null;

        var wanted = relevant(!club.isPrivate);
        var held = actor.BaseRelationships();
        if (actor.UserId is not int userId)
            return new ClubRelationship(clubId, club.isPrivate, held);

        if (club.UserId == userId)
            held |= Owner;

        if ((held & wanted) == 0 && (wanted & (Manager | Volunteer)) != 0)
        {
            var roles = await _db.ClubStaff
                .AsNoTracking()
                .Where(staff => staff.ClubId == clubId && staff.UserId == userId)
                .Select(staff => staff.Role)
                .ToListAsync(cancellationToken);
            foreach (var role in roles)
            {
                held |= role switch
                {
                    ClubStaffRole.Manager => Manager,
                    ClubStaffRole.Volunteer => Volunteer,
                    _ => None,
                };
            }
        }

        if ((held & wanted) == 0 && (wanted & Member) != 0
            && await _db.FollowClubs
                .AsNoTracking()
                .AnyAsync(follow => follow.ClubId == clubId && follow.UserId == userId, cancellationToken))
        {
            held |= Member;
        }

        return new ClubRelationship(clubId, club.isPrivate, held);
    }

    /// <summary>
    /// Resolves the caller's relationships to content in a club. Returns <c>null</c> when the club
    /// does not exist. The caller holds <see cref="Author"/> when they wrote the content, and
    /// <see cref="SeniorStaff"/> when their staff rank in the club is above the author's. Staff
    /// roles are read only when <paramref name="relevant"/> needs them and nothing relevant is
    /// already held.
    /// </summary>
    public async Task<ClubRelationship?> ResolveContentAsync(
        ResourceActor actor,
        int clubId,
        int authorUserId,
        ResourceRelationship relevant,
        CancellationToken cancellationToken = default)
    {
        var club = await _db.Clubs
            .AsNoTracking()
            .Where(entry => entry.Id == clubId)
            .Select(entry => new { entry.UserId, entry.isPrivate })
            .FirstOrDefaultAsync(cancellationToken);
        if (club is null)
            return null;

        var held = actor.BaseRelationships();
        if (actor.UserId is not int userId)
            return new ClubRelationship(clubId, club.isPrivate, held);

        if (userId == authorUserId)
            held |= Author;

        if ((held & relevant) != 0 || (relevant & (ResourceRelationship.ClubStaff | SeniorStaff)) == 0)
            return new ClubRelationship(clubId, club.isPrivate, held);

        var roles = await _db.ClubStaff
            .AsNoTracking()
            .Where(staff => staff.ClubId == clubId && (staff.UserId == userId || staff.UserId == authorUserId))
            .Select(staff => new { staff.UserId, staff.Role })
            .ToListAsync(cancellationToken);

        int RankOf(int candidate) => candidate == club.UserId
            ? 3
            : roles.Where(role => role.UserId == candidate)
                .Select(role => role.Role switch
                {
                    ClubStaffRole.Manager => 2,
                    ClubStaffRole.Volunteer => 1,
                    _ => 0,
                })
                .DefaultIfEmpty(0)
                .Max();

        var callerRank = RankOf(userId);
        held |= callerRank switch
        {
            3 => Owner,
            2 => Manager,
            1 => Volunteer,
            _ => None,
        };
        if (callerRank > RankOf(authorUserId))
            held |= SeniorStaff;

        return new ClubRelationship(clubId, club.isPrivate, held);
    }

    /// <summary>The relationships that can satisfy <paramref name="permission"/> on a club that is (or is not) public.</summary>
    public static Func<bool, ResourceRelationship> RelevantTo(Permission permission) =>
        isPublic => permission.Allowed | (isPublic ? permission.AllowedWhenPublic : None);
}

/// <summary>Decides club permissions: <c>club.read</c>, <c>club.participate</c>, <c>club.contribute</c>, <c>club.staff</c>, <c>club.manage</c>, <c>club.own</c>.</summary>
public sealed class ClubPermissionEvaluator : IResourcePermissionEvaluator
{
    private readonly ClubRelationshipResolver _relationships;

    public ClubPermissionEvaluator(ClubRelationshipResolver relationships)
    {
        _relationships = relationships;
    }

    public async Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var club = (ClubResource)resource;
        var relationship = await _relationships.ResolveAsync(
            actor, club.ClubId, ClubRelationshipResolver.RelevantTo(permission), cancellationToken);

        return relationship is null
            ? PermissionDecision.NotFound
            : PermissionDecision.Decide(permission, relationship.Held, resourceIsPublic: !relationship.IsPrivate);
    }
}
