namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// Short-lived, single-use server state for provider linking: link-start states and pending
    /// email-match links. Both live <see cref="Lifetime"/>, the step-up proof window.
    /// </summary>
    public interface IProviderLinkStateStore
    {
        /// <summary>Stores the state and returns the opaque value sent as the OAuth <c>state</c>.</summary>
        Task<string> CreateLinkStateAsync(ProviderLinkState state);

        /// <summary>Returns and deletes the state, or null when it expired, was used, or never existed.</summary>
        Task<ProviderLinkState?> ConsumeLinkStateAsync(string stateToken);

        /// <summary>Stores the pending link and returns the token the browser holds for it.</summary>
        Task<string> CreatePendingEmailLinkAsync(PendingEmailLink pending);

        /// <summary>Returns and deletes the pending link, or null when it expired or was used.</summary>
        Task<PendingEmailLink?> ConsumePendingEmailLinkAsync(string linkToken);

        /// <summary>
        /// Deletes every link-start state and pending email link the account has outstanding, so a
        /// change to its links leaves no flow that was started against the old set.
        /// </summary>
        Task RevokeAllForUserAsync(int userId);
    }

    public static class ProviderLinkStateLifetime
    {
        /// <summary>Matches the step-up proof lifetime: a flow cannot outlive the proof it needs.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    }
}
