using backend.main.shared.attributes.repository;

namespace backend.main.features.auth.oauth.links
{
    public interface IUserIdentityLinkRepository
    {
        /// <summary>The link holding this provider subject, whichever account owns it.</summary>
        Task<UserIdentityLink?> FindBySubjectAsync(string provider, string subject);

        /// <summary>The account's links, in provider order.</summary>
        Task<IReadOnlyList<UserIdentityLink>> ListForUserAsync(int userId);

        /// <summary>
        /// Inserts the link. The unique indexes decide a race: of concurrent inserts for one
        /// subject, or for one account and provider, exactly one is <see cref="IdentityLinkAddResult.Added"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="NoRetryAttribute"/> because a rerun after a commit whose acknowledgement was
        /// lost would report the caller's own link as taken.
        /// </remarks>
        [NoRetry]
        Task<IdentityLinkAddResult> AddAsync(UserIdentityLink link);

        /// <summary>
        /// Records a sign-in through the link and refreshes the display metadata the provider
        /// reported. The subject never changes.
        /// </summary>
        Task TouchAsync(int linkId, string? email, string? displayName, DateTime usedAtUtc);
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
