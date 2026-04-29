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
        private readonly string? _appleClientId;

        private readonly ConfigurationManager<OpenIdConnectConfiguration>? _appleConfigManager;
        private readonly ConfigurationManager<OpenIdConnectConfiguration>? _microsoftConfigManager;

        private static readonly JwtSecurityTokenHandler _jwtHandler = new()
        {
            MapInboundClaims = false
        };

        public OAuthService(HttpClient? httpClient = null)
            : base(httpClient)
        {
            _googleClientId = EnvironmentSetting.GoogleClientId;

            _microsoftClientId = EnvironmentSetting.MicrosoftClientId;
            if (_microsoftClientId != null)
                _microsoftConfigManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever()
                );

            _appleClientId = EnvironmentSetting.AppleClientId;
            if (_appleClientId != null)
                _appleConfigManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    "https://appleid.apple.com/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever()
                );
        }

        public async Task<OAuthUser> VerifyAppleTokenAsync(string appleToken)
        {
            if (_appleClientId == null || _appleConfigManager == null)
                throw new NotAvailableException("Apple OAuth is not available");

            var validationParams = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,

                ValidAudience = _appleClientId,
                ConfigurationManager = _appleConfigManager,

                ValidateIssuer = true,
                ValidIssuer = "https://appleid.apple.com",

                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _jwtHandler.ValidateToken(appleToken, validationParams, out _);

            var sub =
                principal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ??
                throw new UnauthorizedException("Missing Apple sub claim");

            var email =
                principal.Claims.FirstOrDefault(c => c.Type == "email")?.Value ??
                throw new UnauthorizedException("Missing Apple email claim");

            return new OAuthUser(sub, email, email, "apple");
        }

        public async Task<OAuthUser> VerifyGoogleTokenAsync(
            string googleToken,
            string? expectedNonce = null
        )
        {
            if (_googleClientId == null)
                throw new NotAvailableException("Google OAuth is not available");

            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _googleClientId }
            };

            var payload = await ExecuteResilientHttpAsync(async () =>
                await GoogleJsonWebSignature.ValidateAsync(googleToken, settings)
            );

            if (!payload.EmailVerified)
                throw new UnauthorizedException("Google email is not verified");

            if (!string.IsNullOrWhiteSpace(expectedNonce)
                && !string.Equals(payload.Nonce, expectedNonce, StringComparison.Ordinal))
            {
                throw new UnauthorizedException("Google nonce validation failed");
            }

            return new OAuthUser(
                payload.Subject,
                payload.Email,
                payload.Name ?? payload.Email,
                "google"
            );
        }

        public async Task<OAuthUser> VerifyMicrosoftTokenAsync(string microsoftToken)
        {
            if (_microsoftClientId == null || _microsoftConfigManager == null)
                throw new NotAvailableException("Microsoft OAuth is not available");

            var validationParams = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireSignedTokens = true,

                ValidAudience = _microsoftClientId,
                ConfigurationManager = _microsoftConfigManager,

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

            var principal = _jwtHandler.ValidateToken(microsoftToken, validationParams, out _);

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
