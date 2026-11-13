using Microsoft.AspNetCore.Authorization;

namespace backend.main.application.security.authorization;

/// <summary>An ASP.NET Core requirement for one catalog <see cref="Permission"/>.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(Permission permission)
    {
        Permission = permission;
    }

    public Permission Permission
    {
        get;
    }
}

/// <summary>Why a <see cref="PermissionRequirement"/> failed, so the edge can answer like the services do.</summary>
public sealed class PermissionFailureReason : AuthorizationFailureReason
{
    public PermissionFailureReason(IAuthorizationHandler handler, PermissionDecision decision)
        : base(handler, decision.Outcome.ToString())
    {
        Decision = decision;
    }

    public PermissionDecision Decision
    {
        get;
    }
}

/// <summary>
/// Resource-based authorization handler: <c>IAuthorizationService.AuthorizeAsync(user, resource,
/// new PermissionRequirement(permission))</c> evaluates the permission against authoritative
/// database state through <see cref="IResourceAuthorizer"/>.
/// </summary>
public sealed class ResourcePermissionHandler : AuthorizationHandler<PermissionRequirement, IAuthorizationResource>
{
    private readonly IResourceAuthorizer _authorizer;

    public ResourcePermissionHandler(IResourceAuthorizer authorizer)
    {
        _authorizer = authorizer;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement,
        IAuthorizationResource resource)
    {
        var actor = ResourceActor.From(context.User);
        var decision = await _authorizer.EvaluateAsync(actor, requirement.Permission, resource);

        if (decision.IsAllowed)
            context.Succeed(requirement);
        else
            context.Fail(new PermissionFailureReason(this, decision));
    }
}
