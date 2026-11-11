using backend.main.application.environment;
using backend.main.shared.exceptions.http;

using Microsoft.AspNetCore.WebUtilities;

namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// Google: the server redeems the code with its client secret and the PKCE verifier it kept.
    /// </summary>
    public sealed class GoogleProviderLinkFlow : IProviderLinkFlow
    {
        private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

        private readonly IOAuthService _oauthService;

        public GoogleProviderLinkFlow(IOAuthService oauthService)
        {
            _oauthService = oauthService;
        }

        public string Provider => IdentityProviders.Google;

        public bool IsAvailable => _oauthService.IsProviderConfigured(Provider);

        public bool BrowserRedeemsCode => false;

        public string RedirectUri => ProviderLinkRedirects.For(IdentityProviders.Google);

        public string BuildAuthorizationUrl(string state, string nonce, string codeChallenge) =>
            QueryHelpers.AddQueryString(AuthorizeEndpoint, new Dictionary<string, string?>
            {
                ["client_id"] = EnvironmentSetting.GoogleClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "select_account",
            });

        public async Task<OAuthUser> ResolveIdentityAsync(ProviderLinkState state, string? code, string? idToken)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new BadRequestException("The Google authorization code is missing.");

            var token = await _oauthService.ExchangeGoogleCodeAsync(code, state.CodeVerifier, state.RedirectUri);
            return await _oauthService.VerifyGoogleTokenAsync(token, state.Nonce);
        }
    }

    /// <summary>
    /// Microsoft: the app registration is a public SPA client, so the browser redeems the code
    /// with the verifier the server issued and posts the ID token. The server still binds it to
    /// its own state and nonce.
    /// </summary>
    public sealed class MicrosoftProviderLinkFlow : IProviderLinkFlow
    {
        private const string AuthorizeEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

        private readonly IOAuthService _oauthService;

        public MicrosoftProviderLinkFlow(IOAuthService oauthService)
        {
            _oauthService = oauthService;
        }

        public string Provider => IdentityProviders.Microsoft;

        public bool IsAvailable => _oauthService.IsProviderConfigured(Provider);

        public bool BrowserRedeemsCode => true;

        public string RedirectUri => ProviderLinkRedirects.For(IdentityProviders.Microsoft);

        public string BuildAuthorizationUrl(string state, string nonce, string codeChallenge) =>
            QueryHelpers.AddQueryString(AuthorizeEndpoint, new Dictionary<string, string?>
            {
                ["client_id"] = EnvironmentSetting.MicrosoftClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["response_mode"] = "query",
                ["scope"] = "openid profile email",
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "select_account",
            });

        public Task<OAuthUser> ResolveIdentityAsync(ProviderLinkState state, string? code, string? idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                throw new BadRequestException("The Microsoft ID token is missing.");

            return _oauthService.VerifyMicrosoftTokenAsync(idToken, state.Nonce);
        }
    }

    /// <summary>
    /// The callback pages already registered with each provider for sign-in. Linking reuses them,
    /// so no provider configuration changes, and the callback tells the two flows apart by state.
    /// </summary>
    public static class ProviderLinkRedirects
    {
        public static string For(string provider) =>
            $"{EnvironmentSetting.FrontendBaseUrl.TrimEnd('/')}/auth/{provider}";
    }
}
