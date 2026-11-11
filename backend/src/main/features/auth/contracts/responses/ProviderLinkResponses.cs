using System.Text.Json.Serialization;

namespace backend.main.features.auth.contracts.responses
{
    /// <summary>The account's sign-in methods: its password and each linkable provider.</summary>
    public sealed class ProviderLinksResponse
    {
        [JsonPropertyName("hasPassword")]
        public bool HasPassword
        {
            get; set;
        }

        [JsonPropertyName("providers")]
        public required IReadOnlyList<ProviderLinkStatusResponse> Providers
        {
            get; set;
        }
    }

    public sealed class ProviderLinkStatusResponse
    {
        /// <summary><c>google</c> or <c>microsoft</c>; <c>apple</c> is reserved.</summary>
        [JsonPropertyName("provider")]
        public required string Provider
        {
            get; set;
        }

        /// <summary>Whether this deployment can start a link with the provider.</summary>
        [JsonPropertyName("available")]
        public bool Available
        {
            get; set;
        }

        /// <summary>Null when the provider is not linked.</summary>
        [JsonPropertyName("link")]
        public ProviderLinkResponse? Link
        {
            get; set;
        }

        /// <summary>Whether removing the link leaves another way to sign in.</summary>
        [JsonPropertyName("canUnlink")]
        public bool CanUnlink
        {
            get; set;
        }
    }

    /// <summary>A linked provider identity. The provider subject itself is never returned.</summary>
    public sealed class ProviderLinkResponse
    {
        [JsonPropertyName("provider")]
        public required string Provider
        {
            get; set;
        }

        /// <summary>The address the provider last reported. Display only; never used to sign in.</summary>
        [JsonPropertyName("email")]
        public string? Email
        {
            get; set;
        }

        [JsonPropertyName("displayName")]
        public string? DisplayName
        {
            get; set;
        }

        [JsonPropertyName("linkedAtUtc")]
        public DateTime LinkedAtUtc
        {
            get; set;
        }

        /// <summary>Null until the link is first used to sign in.</summary>
        [JsonPropertyName("lastUsedAtUtc")]
        public DateTime? LastUsedAtUtc
        {
            get; set;
        }
    }

    public sealed class ProviderLinkStartResponse
    {
        /// <summary>Where to send the browser. It carries the state, nonce, and PKCE challenge.</summary>
        [JsonPropertyName("authorizationUrl")]
        public required string AuthorizationUrl
        {
            get; set;
        }

        /// <summary>The OAuth <c>state</c>, so the callback can tell this link from a sign-in.</summary>
        [JsonPropertyName("state")]
        public required string State
        {
            get; set;
        }

        /// <summary>
        /// The exact redirect URI the authorization request names. A browser that redeems the
        /// code itself must send this value rather than one it derives from its own configuration.
        /// </summary>
        [JsonPropertyName("redirectUri")]
        public required string RedirectUri
        {
            get; set;
        }

        /// <summary>
        /// The PKCE verifier, only for a provider whose code the browser redeems (Microsoft). Null
        /// for Google, whose verifier never leaves the server.
        /// </summary>
        [JsonPropertyName("codeVerifier")]
        public string? CodeVerifier
        {
            get; set;
        }
    }

    /// <summary>
    /// A provider sign-in matched an existing account by its verified email, but that account
    /// has no provider link. Nothing is linked or signed in until the owner signs in to the
    /// account and confirms with <see cref="LinkToken"/>.
    /// </summary>
    public sealed class OAuthLinkConfirmationResponse
    {
        [JsonPropertyName("linkToken")]
        public required string LinkToken
        {
            get; set;
        }

        [JsonPropertyName("provider")]
        public required string Provider
        {
            get; set;
        }

        [JsonPropertyName("maskedEmail")]
        public required string MaskedEmail
        {
            get; set;
        }

        [JsonPropertyName("expiresAtUtc")]
        public DateTime ExpiresAtUtc
        {
            get; set;
        }
    }
}
