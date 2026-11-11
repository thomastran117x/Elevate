using backend.main.shared.attributes.repository;

namespace backend.main.features.auth.oauth.links
{
    public interface IUserIdentityLinkRepository
    {
        /// <summary>The link holding this provider subject, whichever account owns it.</summary>
        Task<UserIdentityLink?> FindBySubjectAsync(
            string provider,
            string subject,
            CancellationToken cancellationToken = default);

        /// <summary>The account's links, in provider order.</summary>
        Task<IReadOnlyList<UserIdentityLink>> ListForUserAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts the link. The unique indexes decide a race: of concurrent inserts for one
        /// subject, or for one account and provider, exactly one is <see cref="IdentityLinkAddResult.Added"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="NoRetryAttribute"/> because a rerun after a commit whose acknowledgement was
        /// lost would report the caller's own link as taken.
        /// </remarks>
        [NoRetry]
        Task<IdentityLinkAddResult> AddAsync(
            UserIdentityLink link,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a completed sign-in through the link and refreshes the display metadata the
        /// provider reported. The subject never changes.
        /// </summary>
        Task TouchAsync(
            int linkId,
            string? email,
            string? displayName,
            DateTime usedAtUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the account's link for the provider and bumps the account's
        /// <c>AuthVersion</c> in the same commit, unless the link is the account's last way to
        /// sign in. Runs under the account's row lock, so two concurrent removals of an account's
        /// last two links cannot both succeed.
        /// </summary>
        /// <remarks>
        /// The version bump makes every access token, refresh session, step-up proof, and
        /// in-flight sign-in issued before the removal stop validating, including any a sign-in
        /// through the removed provider created. <see cref="NoRetryAttribute"/> for the same
        /// reason as <see cref="AddAsync"/>.
        /// </remarks>
        [NoRetry]
        Task<IdentityLinkRemoveResult> RemoveIfAnotherMethodRemainsAsync(
            int userId,
            string provider,
            CancellationToken cancellationToken = default);
    }

    public enum IdentityLinkRemoveResult
    {
        Removed,
        NotLinked,

        /// <summary>The account has no password and no other link, so the link stays.</summary>
        LastSignInMethod,
    }

    public enum IdentityLinkAddResult
    {
        Added,

        /// <summary>The provider subject is already linked, to this account or another.</summary>
        SubjectTaken,

        /// <summary>The account already links a different subject for this provider.</summary>
        ProviderTaken,
    }
}
