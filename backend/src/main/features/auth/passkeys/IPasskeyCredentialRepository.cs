using backend.main.shared.attributes.repository;

namespace backend.main.features.auth.passkeys
{
    public interface IPasskeyCredentialRepository
    {
        /// <summary>The account's passkeys, oldest first. Read-only.</summary>
        Task<IReadOnlyList<PasskeyCredential>> ListForUserAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>The passkey with this credential id, whichever account owns it.</summary>
        Task<PasskeyCredential?> FindByCredentialIdAsync(
            byte[] credentialId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// How many passkeys the account holds and when the newest was registered. Adding or
        /// removing one changes it; renaming does not.
        /// </summary>
        Task<PasskeyFactorSummary> GetFactorSummaryAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The account's user handle, creating it on first use. Of concurrent first calls one
        /// handle wins and every caller receives it. Null when the account does not exist.
        /// </summary>
        Task<byte[]?> GetOrCreateUserHandleAsync(
            int userId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts the passkey unless the account already holds <paramref name="maxPerUser"/> of
        /// them. The account row serializes concurrent registrations, and the unique credential id
        /// index refuses an id registered before, to this account or any other.
        /// </summary>
        /// <remarks>
        /// <see cref="NoRetryAttribute"/> because the method already retries transient faults
        /// through the execution strategy, and a rerun after a commit whose acknowledgement was
        /// lost would report the caller's own passkey as taken.
        /// </remarks>
        [NoRetry]
        Task<PasskeyAddResult> AddAsync(
            PasskeyCredential credential,
            int maxPerUser,
            CancellationToken cancellationToken = default);

        /// <summary>Renames the account's passkey. False when the account holds no such passkey.</summary>
        Task<bool> RenameAsync(
            int userId,
            int passkeyId,
            string friendlyName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a verified assertion. The counter only ever moves forward, so a slower
        /// concurrent assertion cannot roll it back.
        /// </summary>
        Task RecordUseAsync(
            int passkeyId,
            long signatureCounter,
            bool isBackedUp,
            DateTime usedAtUtc,
            bool counterAnomaly,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records a counter regression on an assertion that was refused for it, without counting
        /// the assertion as a use.
        /// </summary>
        Task FlagCounterAnomalyAsync(
            int passkeyId,
            DateTime detectedAtUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes the account's passkey unless it is the last way left to sign in. The account
        /// row serializes this with other removals and with provider unlinks.
        /// </summary>
        [NoRetry]
        Task<PasskeyRemoveResult> RemoveIfAnotherMethodRemainsAsync(
            int userId,
            int passkeyId,
            CancellationToken cancellationToken = default);
    }

    public sealed record PasskeyFactorSummary(int Count, DateTime? NewestCreatedAtUtc);

    public enum PasskeyAddResult
    {
        Added,
        CredentialTaken,
        LimitReached,
        UserNotFound,
    }

    public enum PasskeyRemoveResult
    {
        Removed,
        NotFound,
        LastSignInMethod,
    }
}
