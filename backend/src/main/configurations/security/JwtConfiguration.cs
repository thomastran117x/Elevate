using System.Security.Claims;
using System.Text;

using backend.main.configurations.environment;
using backend.main.dtos.general;
using backend.main.errors.app;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace backend.main.configurations.security
{
    public static class JwtConfiguration
    {
        private const string ISSUER = "EventXperience";
        private const string AUDIENCE = "EventXperienceConsumers";
        public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
        {
            services.AddAuthentication("Bearer")
                .AddJwtBearer("Bearer", options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = ISSUER,
                        ValidAudience = AUDIENCE,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(EnvironmentSetting.JwtSecretKeyAccess)),
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };

                    options.Events = new JwtBearerEvents
                    {
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

                            var payload = new
                            {
                                status = 401,
                                error = "Unauthorized",
                                message
                            };

                            await context.Response.WriteAsJsonAsync(payload);
                        },

                        OnForbidden = async context =>
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/json";

                            await context.Response.WriteAsJsonAsync(new
                            {
                                status = 403,
                                error = "Forbidden",
                                message = "You do not have permission to access this resource"
                            });
                        }
                    };
                });


            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", p =>
                    p.RequireRole(AuthRoles.Admin));

                options.AddPolicy("OrganizerOnly", p =>
                    p.RequireRole(AuthRoles.Organizer));
            });
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
