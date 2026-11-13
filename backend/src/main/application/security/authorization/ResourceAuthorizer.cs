using backend.main.shared.exceptions.http;

namespace backend.main.application.security.authorization;

/// <summary>
/// Decides one family of permissions from authoritative database state. Registered as a keyed
/// service under its <see cref="ResourceKind"/>.
/// </summary>
public interface IResourcePermissionEvaluator
{
    Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one entry point for resource authorization. MVC actions and hub methods reach it through
/// <see cref="ResourcePermissionHandler"/>; services call it directly so direct and transactional
/// entry points stay protected.
/// </summary>
public interface IResourceAuthorizer
{
    Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default);
}

public sealed class ResourceAuthorizer : IResourceAuthorizer
{
    private readonly IServiceProvider _services;

    // Evaluators are resolved per call rather than injected: they depend on feature services
    // (the event evaluator on the visibility checker, which depends on the club service), and
    // those services depend on this authorizer.
    public ResourceAuthorizer(IServiceProvider services)
    {
        _services = services;
    }

    public Task<PermissionDecision> EvaluateAsync(
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default)
    {
        if (resource.Kind != permission.Resource)
        {
            throw new InvalidOperationException(
                $"Permission '{permission.Name}' is checked against a {permission.Resource} resource, not a {resource.Kind}.");
        }

        var evaluator = _services.GetRequiredKeyedService<IResourcePermissionEvaluator>(permission.Resource);
        return evaluator.EvaluateAsync(actor, permission, resource, cancellationToken);
    }
}

public static class ResourceAuthorizerExtensions
{
    public static async Task<bool> IsAllowedAsync(
        this IResourceAuthorizer authorizer,
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        CancellationToken cancellationToken = default) =>
        (await authorizer.EvaluateAsync(actor, permission, resource, cancellationToken)).IsAllowed;

    /// <summary>
    /// Throws the API's usual error for a refusal: <see cref="ResourceNotFoundException"/> for a
    /// missing or hidden resource, <see cref="UnauthorizedException"/> for an anonymous caller of
    /// a <see cref="PermissionDenial.ChallengeAnonymous"/> permission, otherwise
    /// <see cref="ForbiddenException"/>.
    /// </summary>
    public static async Task EnsureAsync(
        this IResourceAuthorizer authorizer,
        ResourceActor actor,
        Permission permission,
        IAuthorizationResource resource,
        PermissionMessages? messages = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await authorizer.EvaluateAsync(actor, permission, resource, cancellationToken);
        if (decision.IsAllowed)
            return;

        throw PermissionDenialErrors.For(permission, resource, actor, decision, messages);
    }
}

public static class PermissionDenialErrors
{
    public static AppException For(
        Permission permission,
        IAuthorizationResource resource,
        ResourceActor actor,
        PermissionDecision decision,
        PermissionMessages? messages = null)
    {
        if (decision.Outcome == PermissionOutcome.ResourceNotFound || permission.Denial == PermissionDenial.Hide)
            return new ResourceNotFoundException(resource.NotFoundMessage);

        if (permission.Denial == PermissionDenial.ChallengeAnonymous && !actor.IsAuthenticated)
        {
            return new UnauthorizedException(
                messages?.Anonymous ?? permission.AnonymousDenialMessage ?? "Authentication is required.");
        }

        return new ForbiddenException(messages?.Forbidden ?? permission.DenialMessage);
    }
}
