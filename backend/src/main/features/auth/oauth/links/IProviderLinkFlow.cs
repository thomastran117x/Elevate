namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// One provider's half of explicit linking: where to send the browser, and how to turn what
    /// comes back into a verified identity. Another provider (Apple) is one more implementation.
    /// </summary>
    public interface IProviderLinkFlow
    {
        /// <summary>A name from <see cref="IdentityProviders"/>.</summary>
        string Provider
        {
            get;
        }

        /// <summary>Whether the provider is configured on this deployment.</summary>
        bool IsAvailable
        {
            get;
        }

        /// <summary>
        /// Whether the browser redeems the authorization code itself and so must be handed the
        /// PKCE verifier. When false, the verifier never leaves the server.
        /// </summary>
        bool BrowserRedeemsCode
        {
            get;
        }

        /// <summary>The exact redirect URI registered with the provider for this deployment.</summary>
        string RedirectUri
        {
            get;
        }

        string BuildAuthorizationUrl(string state, string nonce, string codeChallenge);

        /// <summary>
        /// Validates what the callback returned against the stored state and returns the identity.
        /// The token's signature, issuer, audience, lifetime, and nonce are all checked.
        /// </summary>
        Task<OAuthUser> ResolveIdentityAsync(ProviderLinkState state, string? code, string? idToken);
    }
}
