namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// The external identity providers an account can link, by the name stored in
    /// <see cref="UserIdentityLink.Provider"/> and used in routes and responses.
    /// </summary>
    /// <remarks>
    /// Names are a contract with stored rows and the frontend, so they are never renamed. Apple is
    /// reserved for a later flow; it is not offered for linking until that flow exists.
    /// </remarks>
    public static class IdentityProviders
    {
        public const string Google = "google";
        public const string Microsoft = "microsoft";
        public const string Apple = "apple";

        /// <summary>The providers an account can link or unlink today, in display order.</summary>
        public static readonly IReadOnlyList<string> Linkable = [Google, Microsoft];

        /// <summary>
        /// Resolves a route or request value to a linkable provider name, ignoring case.
        /// </summary>
        public static bool TryParseLinkable(string? value, out string provider)
        {
            provider = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (var candidate in Linkable)
            {
                if (string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    provider = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
