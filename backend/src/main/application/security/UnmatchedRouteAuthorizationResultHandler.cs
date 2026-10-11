using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace backend.main.application.security
{
    /// <summary>
    /// Lets requests that matched no route continue to the JSON 404 handler instead of being
    /// challenged by the fallback policy.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core applies <see cref="AuthorizationOptions.FallbackPolicy"/> even when routing
    /// found no endpoint, so without this an anonymous request for an unknown route, or for one
    /// removed by a disabled feature flag, would answer 401 rather than 404. Nothing can execute
    /// for such a request, so passing it through exposes nothing. The framework's 405 endpoint is
    /// not a <see cref="RouteEndpoint"/> either and is treated the same way. Every routed endpoint
    /// is authorized normally.
    /// </remarks>
    public sealed class UnmatchedRouteAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            if (!authorizeResult.Succeeded && context.GetEndpoint() is not RouteEndpoint)
                return next(context);

            return _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
