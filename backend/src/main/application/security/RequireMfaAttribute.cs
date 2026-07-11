using System.Security.Claims;

using backend.main.features.auth;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.token;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace backend.main.application.security
{
    /// <summary>
    /// Requires the caller to have completed a fresh in-session MFA verification
    /// (see <see cref="ISessionMfaVerificationService"/>) before the route runs.
    /// The verification is bound to the access token's <c>sid</c> claim and lasts
    /// for the remainder of the session. Callers that have not verified receive a
    /// <c>403</c> with the distinguishable <c>MFA_REQUIRED</c> error code.
    /// Apply after <c>[Authorize]</c> — it assumes an authenticated principal.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class RequireMfaAttribute : TypeFilterAttribute
    {
        public RequireMfaAttribute()
            : base(typeof(RequireMfaFilter))
        {
        }

        internal sealed class RequireMfaFilter : IAsyncAuthorizationFilter
        {
            public const string MfaRequiredCode = "MFA_REQUIRED";

            private readonly ISessionMfaVerificationService _sessionMfaVerificationService;
            private readonly SeedAccountBypassPolicy _seedAccountBypassPolicy;

            public RequireMfaFilter(
                ISessionMfaVerificationService sessionMfaVerificationService,
                SeedAccountBypassPolicy seedAccountBypassPolicy
            )
            {
                _sessionMfaVerificationService = sessionMfaVerificationService;
                _seedAccountBypassPolicy = seedAccountBypassPolicy;
            }

            public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
            {
                var user = context.HttpContext.User;
                var email = user.FindFirst(ClaimTypes.Name)?.Value;

                // Keep dev/test seed accounts usable without a real second factor.
                if (_seedAccountBypassPolicy.IsBypassEnabledFor(email))
                    return;

                var sessionId = user.FindFirst(TokenService.SessionIdClaimType)?.Value;
                if (await _sessionMfaVerificationService.IsSessionVerifiedAsync(sessionId))
                    return;

                context.Result = new ObjectResult(
                    ApiResponse<object?>.Failure(
                        "Multi-factor verification is required to access this resource.",
                        MfaRequiredCode
                    )
                )
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };
            }
        }
    }
}
