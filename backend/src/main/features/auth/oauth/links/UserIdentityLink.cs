namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// One external identity linked to an account: the provider's immutable subject, mapped to
    /// exactly one local account.
    /// </summary>
    /// <remarks>
    /// The subject, never the email, is the identity. <see cref="Email"/> and
    /// <see cref="DisplayName"/> are display metadata the provider last reported and are never used
    /// to resolve a sign-in. A provider subject maps to at most one account, and an account holds
    /// at most one link per provider; both are unique indexes.
    /// </remarks>
    public sealed class UserIdentityLink
    {
        public int Id
        {
            get; set;
        }

        public int UserId
        {
            get; set;
        }

        /// <summary>A name from <see cref="IdentityProviders"/>.</summary>
        public required string Provider
        {
            get; set;
        }

        /// <summary>The provider's subject (<c>sub</c>) claim. Never changes once linked.</summary>
        public required string Subject
        {
            get; set;
        }

        public string? Email
        {
            get; set;
        }

        public string? DisplayName
        {
            get; set;
        }

        public DateTime LinkedAtUtc
        {
            get; set;
        }

        public DateTime? LastUsedAtUtc
        {
            get; set;
        }
    }
}
