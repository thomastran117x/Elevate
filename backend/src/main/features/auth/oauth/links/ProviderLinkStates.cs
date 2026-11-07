namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// What a link-start issued and the matching link-complete must present: the OAuth
    /// <c>state</c>, <c>nonce</c>, and PKCE verifier, bound to the session that started it.
    /// </summary>
    /// <remarks>
    /// Held only on the server, keyed by a hash of the state, and consumed once. The redirect URI
    /// is the server's, so a client can never choose where the provider sends the code.
    /// </remarks>
    public sealed class ProviderLinkState
    {
        public required string Provider
        {
            get; init;
        }

        public int UserId
        {
            get; init;
        }

        public required string SessionId
        {
            get; init;
        }

        public int AuthVersion
        {
            get; init;
        }

        public required string Nonce
        {
            get; init;
        }

        public required string CodeVerifier
        {
            get; init;
        }

        public required string RedirectUri
        {
            get; init;
        }

        public DateTime CreatedAtUtc
        {
            get; init;
        }
    }

    /// <summary>
    /// A provider identity whose verified email matched an account with no link. It is linked
    /// only once the owner signs in to that account and confirms.
    /// </summary>
    public sealed class PendingEmailLink
    {
        public int UserId
        {
            get; init;
        }

        public required string Provider
        {
            get; init;
        }

        public required string Subject
        {
            get; init;
        }

        public required string Email
        {
            get; init;
        }

        public string? Name
        {
            get; init;
        }

        public DateTime CreatedAtUtc
        {
            get; init;
        }
    }
}
