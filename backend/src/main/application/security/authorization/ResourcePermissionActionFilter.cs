using System.Security.Claims;

using backend.main.shared.exceptions.http;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;

namespace backend.main.application.security.authorization;

/// <summary>
/// Enforces <see cref="RequirePermissionAttribute"/> on MVC actions through
/// <see cref="IAuthorizationService"/>.
/// </summary>
/// <remarks>
/// Registered as a global action filter, so it runs after authentication, the fallback policy and
/// <c>[ApiController]</c> model validation: a malformed request is still answered with its 400.
/// A refusal is written with the same envelope the services produce through
/// <see cref="HandleError"/>, and the denial observer records it as a resource-access denial.
/// </remarks>
public sealed class ResourcePermissionActionFilter : IAsyncActionFilter
{
    private readonly IAuthorizationService _authorization;

    public ResourcePermissionActionFilter(IAuthorizationService authorization)
    {
        _authorization = authorization;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var declaration in context.ActionDescriptor.EndpointMetadata.OfType<RequirePermissionAttribute>())
        {
            context.ActionArguments.TryGetValue(declaration.ResourceParameter, out var value);
            value ??= context.RouteData.Values.GetValueOrDefault(declaration.ResourceParameter);

            var refusal = await ResourcePermissionEdge.CheckAsync(
                _authorization, context.HttpContext.User, declaration, value);
            if (refusal is null)
                continue;

            context.Result = HandleError.Resolve(refusal);
            return;
        }

        await next();
    }
}

/// <summary>The edge check shared by the MVC and SignalR filters.</summary>
public static class ResourcePermissionEdge
{
    /// <summary>
    /// Returns the error to answer with, or <c>null</c> when the caller may proceed. An id that
    /// does not parse, or a resource that does not exist, is left to the action to report.
    /// </summary>
    public static async Task<AppException?> CheckAsync(
        IAuthorizationService authorization,
        ClaimsPrincipal? user,
        RequirePermissionAttribute declaration,
        object? value)
    {
        var resource = declaration.CreateResource(value);
        if (resource is null)
            return null;

        var principal = user ?? new ClaimsPrincipal();
        var permission = declaration.Permission;
        var result = await authorization.AuthorizeAsync(principal, resource, new PermissionRequirement(permission));
        if (result.Succeeded)
            return null;

        var decision = result.Failure?.FailureReasons.OfType<PermissionFailureReason>().FirstOrDefault()?.Decision
            ?? new PermissionDecision(PermissionOutcome.Denied, ResourceRelationship.None);
        if (decision.Outcome == PermissionOutcome.ResourceNotFound)
            return null;

        return PermissionDenialErrors.For(
            permission, resource, ResourceActor.From(principal), decision, declaration.Messages);
    }
}
