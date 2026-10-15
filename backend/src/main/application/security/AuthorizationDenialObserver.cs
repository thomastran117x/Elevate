using System.Security.Claims;

using backend.main.features.auth.events;

namespace backend.main.application.security
{
    public static class AuthorizationDenialObserverHandler
    {
        public static IApplicationBuilder UseAuthorizationDenialObserver(this IApplicationBuilder app)
        {
            return app.UseMiddleware<AuthorizationDenialObserverMiddleware>();
        }
    }

    /// <summary>
    /// Records every 403 response as an <c>authz.denied</c> security event.
    /// </summary>
    /// <remarks>
    /// Denials come from several places: authorization policies through
    /// <c>JwtBearerEvents.OnForbidden</c>, <c>[RequireMfa]</c>, and services throwing
    /// <c>ForbiddenException</c>, which controllers or the global exception handler turn into a
    /// response. Observing the status code as the response starts covers all of them in one place.
    /// Producers that know why they refused say so through <see cref="AuthSecurityRequestFeature"/>;
    /// a 403 another security event already describes is not counted twice. A 401 is not a denial
    /// of an authenticated caller and is left out: expired access tokens produce them constantly.
    /// </remarks>
    public sealed class AuthorizationDenialObserverMiddleware
    {
        private static readonly HashSet<string> KnownMethods =
            new(StringComparer.Ordinal) { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

        private readonly RequestDelegate _next;
        private readonly IAuthSecurityEventEmitter _securityEvents;

        public AuthorizationDenialObserverMiddleware(
            RequestDelegate next,
            IAuthSecurityEventEmitter securityEvents)
        {
            _next = next;
            _securityEvents = securityEvents;
        }

        public Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                Observe(context);
                return Task.CompletedTask;
            });

            return _next(context);
        }

        internal void Observe(HttpContext context)
        {
            if (context.Response.StatusCode != StatusCodes.Status403Forbidden)
                return;

            var feature = context.Features.Get<AuthSecurityRequestFeature>();
            if (feature?.EventRecorded == true)
                return;

            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.AuthorizationDenied,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = feature?.DenialReason ?? AuthSecurityReason.ResourceAccess,
                // A denial is about the caller, so here, unlike elsewhere, the account comes from the token.
                UserId = int.TryParse(
                    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    out var userId)
                    ? userId
                    : null,
                Route = DescribeRoute(context),
            });
        }

        /// <summary>
        /// The method and route template, both bounded by the application's own routes. The
        /// request path is never used: it can carry identifiers and arbitrary text.
        /// </summary>
        private static string? DescribeRoute(HttpContext context)
        {
            if (context.GetEndpoint() is not RouteEndpoint endpoint)
                return null;

            var method = KnownMethods.Contains(context.Request.Method) ? context.Request.Method : "OTHER";
            return $"{method} {endpoint.RoutePattern.RawText}";
        }
    }
}
