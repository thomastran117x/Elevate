namespace backend.main.features.auth.oauth
{
    public interface IOAuthService
    {
        Task<string> ExchangeGoogleCodeAsync(string code, string codeVerifier, string redirectUri);
        Task<OAuthUser> VerifyGoogleTokenAsync(string googleToken, string? expectedNonce = null);
        Task<OAuthUser> VerifyMicrosoftTokenAsync(
            string microsoftToken,
            string? expectedNonce = null
        );
        Task<OAuthUser> VerifyAppleTokenAsync(string appleToken);

        /// <summary>
        /// Whether the provider (a name from <c>IdentityProviders</c>) is configured well enough
        /// to sign in and link: Google needs its client id and secret, Microsoft its client id.
        /// </summary>
        bool IsProviderConfigured(string provider);
    }
}
