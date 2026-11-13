using System.Security.Claims;

using backend.main.application.bootstrap;
using backend.main.application.security.authorization;
using backend.main.features.auth;
using backend.main.features.auth.events;
using backend.main.features.auth.sessions;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.app;
using backend.main.shared.other;
using backend.main.shared.responses;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace backend.main.application.security
{
    public static class JwtConfiguration
    {
        private const string ISSUER = "EventXperience";
        private const string AUDIENCE = "EventXperienceConsumers";
        /// <summary>
        /// Access-token validation: issuer, audience, lifetime, and a signature by any key in the
        /// ring, so tokens signed before an overlapping rotation keep working until they expire.
        /// </summary>
        public static TokenValidationParameters CreateTokenValidationParameters(JwtSigningKeyRing signingKeys) =>
            new()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = ISSUER,
                ValidAudience = AUDIENCE,
                IssuerSigningKeys = signingKeys.ValidationKeys,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };

        public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
        {
            var signingKeys = JwtSigningKeyRing.FromEnvironment();
            services.AddSingleton(signingKeys);

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters = CreateTokenValidationParameters(signingKeys);

                    options.Events = new JwtBearerEvents
                    {
                        // Browsers cannot set an Authorization header on a WebSocket handshake,
                        // so SignalR sends the token as a query parameter instead. Accept it only
                        // for hub paths, never for the regular API surface.
                        OnMessageReceived = context =>
                        {
                            if (string.IsNullOrEmpty(context.Token)
                                && context.Request.Path.StartsWithSegments(RoutePaths.ApiHubsPath))
                            {
                                var queryToken = context.Request.Query["access_token"].ToString();
                                if (!string.IsNullOrEmpty(queryToken))
                                    context.Token = queryToken;
                            }

                            return Task.CompletedTask;
                        },

                        OnChallenge = async context =>
                        {
                            context.HandleResponse();

                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";

                            var message = context.ErrorDescription switch
                            {
                                null => "Missing access token",
                                _ => "Invalid or expired access token"
                            };

                            await context.Response.WriteAsJsonAsync(
                                ApiResponse<object?>.Failure(message, "UNAUTHORIZED")
                            );
                        },

                        OnForbidden = async context =>
                        {
                            AuthSecurityRequestFeature.MarkDenial(context.HttpContext, AuthSecurityReason.Policy);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/json";

                            await context.Response.WriteAsJsonAsync(
                                ApiResponse<object?>.Failure(
                                    "You do not have permission to access this resource",
                                    "FORBIDDEN"
                                )
                            );
                        },

                        OnTokenValidated = async context =>
                        {
                            string? idClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                            string? authVersionClaim = context.Principal?.FindFirst(TokenService.AuthVersionClaimType)?.Value;
                            string? sessionId = context.Principal?.FindFirst(TokenService.SessionIdClaimType)?.Value;

                            if (!int.TryParse(idClaim, out var userId)
                                || !int.TryParse(authVersionClaim, out var authVersion)
                                || string.IsNullOrWhiteSpace(sessionId))
                            {
                                context.Fail("Invalid token payload.");
                                return;
                            }

                            // The session registry, not Redis, decides whether the token's session
                            // is alive, so a revoked session's access token stops working at once
                            // and keeps being refused through a cache outage. A session from
                            // before the registry has no row until its next refresh records it.
                            var services = context.HttpContext.RequestServices;
                            var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
                            var state = await services
                                .GetRequiredService<IAuthSessionRepository>()
                                .GetAccessTokenStateAsync(userId, sessionId, now);

                            if (state == null
                                || state.IsDisabled
                                || state.AuthVersion != authVersion
                                || !state.SessionActive)
                            {
                                context.Fail("This access token is no longer valid.");
                            }
                        }
                    };
                });


            services.AddAuthorization(options =>
            {
                // Authenticated by default: an endpoint is public only when it opts out with
                // [AllowAnonymous], so a forgotten attribute fails closed instead of open.
                var authenticated = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build();
                options.DefaultPolicy = authenticated;
                options.FallbackPolicy = authenticated;

                options.AddPolicy(AuthorizationPolicies.AdminOnly, p =>
                    p.RequireRole(AuthRoles.Admin));

                options.AddPolicy(AuthorizationPolicies.OrganizerOnly, p =>
                    p.RequireRole(AuthRoles.Organizer));
            });
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, UnmatchedRouteAuthorizationResultHandler>();
            services.AddResourceAuthorization();
            return services;
        }
    }
    public static class ClaimsPrincipalExtensions
    {
        public static UserIdentityPayload GetUserPayload(this ClaimsPrincipal user)
        {
            string? idClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            string? emailClaim = user.FindFirst(ClaimTypes.Name)?.Value;
            string? roleClaim = user.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(idClaim) || string.IsNullOrEmpty(emailClaim) || string.IsNullOrEmpty(roleClaim))
            {
                throw new InvalidTokenPayloadException();
            }

            return new UserIdentityPayload(
                int.Parse(idClaim),
                emailClaim,
                AuthRoles.NormalizeStored(roleClaim)
            );
        }
    }
}


