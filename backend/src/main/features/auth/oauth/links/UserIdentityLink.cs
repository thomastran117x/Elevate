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
        public const int ProviderMaxLength = 32;
        public const int SubjectMaxLength = 255;
        public const int EmailMaxLength = 320;
        public const int DisplayNameMaxLength = 255;

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

        /// <summary>
        /// Display metadata as it can be stored: trimmed, blank as null, and cut to the column.
        /// Providers do not bound these claims, and an oversized one must not fail a sign-in.
        /// </summary>
        public static string? ClampMetadata(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    /// <summary>
    /// A sign-in through an existing link, recorded on the link only once the sign-in has
    /// actually completed: the account is enabled, any step-up is satisfied, and a session exists.
    /// </summary>
    public sealed record ProviderLinkUse(int LinkId, string? Email, string? DisplayName);
}
