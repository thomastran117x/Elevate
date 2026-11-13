namespace backend.main.application.security.authorization;

/// <summary>Decides <c>user.self-or-admin</c>: the caller is the user, or a global Admin.</summary>
public sealed class UserPermissionEvaluator : IResourcePermissionEvaluator
{
    public Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var held = actor.BaseRelationships();
        if (actor.UserId == ((UserResource)resource).UserId)
            held |= ResourceRelationship.Self;

        return Task.FromResult(PermissionDecision.Decide(permission, held, resourceIsPublic: false));
    }
}

/// <summary>Decides <c>content.author</c> for content the service has already loaded.</summary>
public sealed class AuthoredContentPermissionEvaluator : IResourcePermissionEvaluator
{
    public Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        var held = actor.BaseRelationships();
        if (actor.UserId == ((AuthoredContentResource)resource).AuthorUserId)
            held |= ResourceRelationship.Author;

        return Task.FromResult(PermissionDecision.Decide(permission, held, resourceIsPublic: false));
    }
}
