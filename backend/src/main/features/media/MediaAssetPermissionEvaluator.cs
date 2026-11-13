using backend.main.application.security.authorization;
using backend.main.features.clubs.access;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.media;

/// <summary>
/// Decides <c>media.view</c>: the uploader, or a manager of the club the asset was uploaded for.
/// An asset with no club is visible to its uploader alone, Admin included.
/// </summary>
public sealed class MediaAssetPermissionEvaluator : IResourcePermissionEvaluator
{
    private readonly AppDatabaseContext _db;
    private readonly ClubRelationshipResolver _relationships;

    public MediaAssetPermissionEvaluator(AppDatabaseContext db, ClubRelationshipResolver relationships)
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
        var publicId = ((MediaAssetResource)resource).PublicId;
        var asset = await _db.MediaAssets
            .AsNoTracking()
            .Where(entry => entry.PublicId == publicId)
            .Select(entry => new { entry.OwnerUserId, entry.ClubId })
            .FirstOrDefaultAsync(cancellationToken);
        if (asset is null)
            return PermissionDecision.NotFound;

        if (actor.UserId is int userId && asset.OwnerUserId == userId)
            return PermissionDecision.Decide(permission, actor.BaseRelationships() | ResourceRelationship.Uploader, false);

        if (asset.ClubId is not int clubId)
            return PermissionDecision.Decide(permission, actor.BaseRelationships() & ~ResourceRelationship.Admin, false);

        var relationship = await _relationships.ResolveAsync(
            actor, clubId, ClubRelationshipResolver.RelevantTo(permission), cancellationToken);
        var held = relationship?.Held ?? actor.BaseRelationships() & ~ResourceRelationship.Admin;
        return PermissionDecision.Decide(permission, held, false);
    }
}
