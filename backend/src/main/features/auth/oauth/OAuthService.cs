using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

using backend.main.application.environment;
using backend.main.shared.exceptions.http;
using backend.main.shared.http;

using Google.Apis.Auth;

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace backend.main.features.auth.oauth
{
    public class OAuthService : BaseService, IOAuthService
    {
        /// <summary>The tenant id Microsoft issues personal (consumer) account tokens from.</summary>
        private const string MicrosoftConsumerTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";

        private readonly string? _googleClientId;
        private readonly string? _googleClientSecret;
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
            _googleClientSecret = EnvironmentSetting.GoogleClientSecret;

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

        public async Task<string> ExchangeGoogleCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri
        )
        {
            if (_googleClientId == null || _googleClientSecret == null)
                throw new NotAvailableException("Google OAuth is not available");

            var response = await Http.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(
                [
                    new("client_id", _googleClientId),
                    new("client_secret", _googleClientSecret),
                    new("code", code),
                    new("code_verifier", codeVerifier),
                    new("redirect_uri", redirectUri),
                    new("grant_type", "authorization_code"),
                ])
            );

            // Google's error body is not echoed: it is provider content the client has no use for.
            if (!response.IsSuccessStatusCode)
                throw new UnauthorizedException("Google code exchange failed.");

            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            var idToken = data.TryGetProperty("id_token", out var idTokenElement)
                ? idTokenElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(idToken))
                throw new UnauthorizedException("No id_token returned from Google.");

            return idToken;
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

            var principal = ValidateProviderToken(appleToken, validationParams, "Invalid Apple token.");

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

            GoogleJsonWebSignature.Payload payload;
            try
            {
                // Only fetching Google's signing keys can fail transiently; the resilience policy
                // retries those faults and lets a rejected token through on the first attempt.
                payload = await ExecuteResilientHttpAsync(async () =>
                    await GoogleJsonWebSignature.ValidateAsync(googleToken, settings)
                );
            }
            catch (InvalidJwtException)
            {
                throw new UnauthorizedException("Invalid Google token.");
            }

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
                "google",
                emailVerified: true
            );
        }

        public async Task<OAuthUser> VerifyMicrosoftTokenAsync(
            string microsoftToken,
            string? expectedNonce = null
        )
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

            var principal = ValidateProviderToken(microsoftToken, validationParams, "Invalid Microsoft token.");

            var nonce = principal.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (!string.IsNullOrWhiteSpace(expectedNonce)
                && !string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            {
                throw new UnauthorizedException("Microsoft nonce validation failed");
            }

            // preferred_username is a work or school UPN that the tenant administrator sets, and
            // email is mutable, so neither proves the caller controls the mailbox. Only email is
            // read, and it counts as verified only when Microsoft vouches for it.
            var email = principal.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            if (string.IsNullOrWhiteSpace(email))
                email = null;

            var name =
                principal.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? email ?? string.Empty;

            var sub =
                principal.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ??
                throw new UnauthorizedException("Missing Microsoft sub claim");

            var emailVerified = email != null && IsMicrosoftEmailVerified(principal);

            return new OAuthUser(sub, email, name, "microsoft", emailVerified);
        }

        /// <summary>
        /// Validates a provider ID token, reporting a token that fails validation as 401.
        /// </summary>
        /// <remarks>
        /// An invalid, expired, wrongly signed, or wrong-audience token is a bad credential, not a
        /// service fault, so it gets the generic message and never the validation exception's text.
        /// A failure to fetch the provider's signing keys is not caught here and stays a fault.
        /// </remarks>
        private static ClaimsPrincipal ValidateProviderToken(
            string token,
            TokenValidationParameters validationParams,
            string rejectionMessage)
        {
            try
            {
                return _jwtHandler.ValidateToken(token, validationParams, out _);
            }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
            {
                throw new UnauthorizedException(rejectionMessage);
            }
        }

        /// <summary>
        /// Personal Microsoft accounts verify their address at sign-up. A work or school address
        /// is verified only when the token carries the xms_edov optional claim set to true,
        /// meaning the tenant has proven it owns the email's domain.
        /// </summary>
        private static bool IsMicrosoftEmailVerified(ClaimsPrincipal principal)
        {
            var tenantId = principal.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
            if (string.Equals(tenantId, MicrosoftConsumerTenantId, StringComparison.OrdinalIgnoreCase))
                return true;

            var domainOwnerVerified = principal.Claims.FirstOrDefault(c => c.Type == "xms_edov")?.Value;
            return string.Equals(domainOwnerVerified, "true", StringComparison.OrdinalIgnoreCase)
                || domainOwnerVerified == "1";
        }
    }
}
