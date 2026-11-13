using backend.main.application.security.authorization;

namespace backend.main.features.clubs.access;

/// <summary>
/// Decides permissions on content in a club (<c>post.manage</c>, <c>content.remove</c>): the
/// author, the club's staff by rank, and admins. Ranks are read from the database on every call,
/// so a demoted or removed moderator loses the power on their next request.
/// </summary>
public sealed class ClubContentPermissionEvaluator : IResourcePermissionEvaluator
{
    private readonly ClubRelationshipResolver _relationships;

    public ClubContentPermissionEvaluator(ClubRelationshipResolver relationships)
    {
        _relationships = relationships;
    }

    public async Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var content = (ClubContentResource)resource;
        var relationship = await _relationships.ResolveContentAsync(
            actor,
            content.ClubId,
            content.AuthorUserId,
            permission.Allowed | permission.AllowedWhenPublic,
            cancellationToken);

        return relationship is null
            ? PermissionDecision.NotFound
            : PermissionDecision.Decide(permission, relationship.Held, resourceIsPublic: !relationship.IsPrivate);
    }
}
