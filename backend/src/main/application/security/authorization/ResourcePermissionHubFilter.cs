using System.Reflection;

using backend.main.application.features;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace backend.main.application.security.authorization;

/// <summary>
/// Enforces <see cref="FeatureGateAttribute"/> and <see cref="RequirePermissionAttribute"/> on hub
/// methods, the SignalR counterpart of the MVC feature-gate convention and
/// <see cref="ResourcePermissionActionFilter"/>.
/// </summary>
/// <remarks>
/// Feature flags are checked first, so a disabled surface answers the same way whoever calls it.
/// Refusals become a <see cref="HubException"/> carrying the same message the REST endpoint uses.
/// </remarks>
public sealed class ResourcePermissionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var method = invocationContext.HubMethod;
        // Resolved per invocation: the authorization handlers are scoped to the hub call.
        var services = invocationContext.ServiceProvider;
        var featureFlags = services.GetRequiredService<IFeatureFlagEvaluator>();
        var authorization = services.GetRequiredService<IAuthorizationService>();

        foreach (var gate in method.GetCustomAttributes<FeatureGateAttribute>(inherit: true))
        {
            if (!featureFlags.IsEnabled(gate.FeatureKey))
                throw new HubException($"The '{gate.FeatureKey}' feature is disabled.");
        }

        var parameters = method.GetParameters();
        foreach (var declaration in method.GetCustomAttributes<RequirePermissionAttribute>(inherit: true))
        {
            var index = Array.FindIndex(parameters, parameter =>
                string.Equals(parameter.Name, declaration.ResourceParameter, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"Hub method '{method.Name}' declares '{declaration.PermissionName}' on missing parameter '{declaration.ResourceParameter}'.");
            }

            var value = index < invocationContext.HubMethodArguments.Count
                ? invocationContext.HubMethodArguments[index]
                : null;
            var refusal = await ResourcePermissionEdge.CheckAsync(
                authorization, invocationContext.Context.User, declaration, value);
            if (refusal is not null)
                throw new HubException(refusal.Message);
        }

        return await next(invocationContext);
    }
}
