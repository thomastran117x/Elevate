using System.IdentityModel.Tokens.Jwt;

using backend.main.configurations.environment;
using backend.main.exceptions.http;
using backend.main.models.other;
using backend.main.services.interfaces;

using Google.Apis.Auth;

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace backend.main.services.implementation
{
    public class OAuthService : BaseService, IOAuthService
    {
        private readonly string? _googleClientId;
        private readonly string? _microsoftClientId;

        public OAuthService(HttpClient? httpClient = null)
            : base(httpClient)
        {
            _googleClientId = EnvironmentSetting.GoogleClientId;
            _microsoftClientId = EnvironmentSetting.MicrosoftClientId;
        }

        public Task<OAuthUser> VerifyAppleTokenAsync(string appleToken)
        {
            throw new backend.main.exceptions.http.NotImplementedException();
        }

        public async Task<OAuthUser> VerifyGoogleTokenAsync(string googleToken)
        {
            if (_googleClientId == null)
                throw new NotAvaliableException("Google OAuth is not available");

            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _googleClientId }
            };

            var payload = await ExecuteResilientHttpAsync(async () =>
                await GoogleJsonWebSignature.ValidateAsync(googleToken, settings)
            );

            return new OAuthUser(
                payload.Subject,
                payload.Email,
                payload.Name ?? payload.Email,
                "google"
            );
        }

        public async Task<OAuthUser> VerifyMicrosoftTokenAsync(string microsoftToken)
        {
            if (_microsoftClientId == null)
                throw new NotAvaliableException("Microsoft OAuth is not available");

            var authority = "https://login.microsoftonline.com/common/v2.0";

            var configManager =
                new ConfigurationManager<OpenIdConnectConfiguration>(
                    $"{authority}/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever()
                );

            var validationParams = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,

                ValidAudience = _microsoftClientId,
                ConfigurationManager = configManager,

                ValidateIssuer = true,
                IssuerValidator = (issuer, token, parameters) =>
                {
                    if (issuer.StartsWith("https://login.microsoftonline.com/")
                        && issuer.EndsWith("/v2.0"))
                    {
                        return issuer;
                    }

                    throw new SecurityTokenInvalidIssuerException(
                        $"Invalid issuer: {issuer}");
                },

                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var handler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false
            };

            var principal = handler.ValidateToken(
                microsoftToken,
                validationParams,
                out _
            );

            var email =
                principal.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value ??
                principal.Claims.FirstOrDefault(c => c.Type == "email")?.Value ??
                throw new UnauthorizedException("Missing Microsoft email claim");

            var name =
                principal.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? email;

            var sub =
                principal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ??
                throw new UnauthorizedException("Missing Microsoft sub claim");

            return new OAuthUser(sub, email, name, "microsoft");
        }
    }
}
