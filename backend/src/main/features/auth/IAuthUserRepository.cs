using backend.main.features.auth.contracts;
using backend.main.features.profile;
using backend.main.features.profile.contracts;

namespace backend.main.features.auth
{
    public interface IAuthUserRepository
    {
        Task<User> CreateUserAsync(User user);
        Task<User?> UpdateUserAsync(int id, User updated);
        Task<UserStatusRecord?> UpdateUserStatusAsync(int id, bool isDisabled, string? disabledReason);
        Task<bool> IncrementAuthVersionAsync(int id);

        /// <summary>
        /// Sets a new password and bumps <c>AuthVersion</c> in one statement, so access tokens
        /// issued under the old password stop validating in the same commit that changes it.
        /// </summary>
        Task<bool> SetPasswordAndBumpAuthVersionAsync(
            int id,
            string passwordHash,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the stored password hash only if it still equals <paramref name="expectedHash"/>,
        /// in a single conditional update. Used to upgrade an obsolete hash after a successful login:
        /// the password itself is unchanged, so <c>AuthVersion</c> is left alone and existing
        /// sessions stay valid. Returns false when the hash changed in the meantime (a concurrent
        /// login already upgraded it, or the password was reset), in which case nothing is written.
        /// </summary>
        Task<bool> TryReplacePasswordHashAsync(
            int id,
            string expectedHash,
            string newHash,
            CancellationToken cancellationToken = default);
        Task<User?> GetUserAsync(int id);
        Task<UserAuthRecord?> GetAuthByUsernameAsync(string username);
        Task<UserAuthRecord?> GetAuthByEmailAsync(string email);
        /// <summary>
        /// Credentials looked up by account id rather than by address, for callers whose
        /// whole purpose is that the address is about to change.
        /// </summary>
        Task<UserAuthRecord?> GetAuthByIdAsync(int id);
        Task<UserRecoveryRecord?> GetRecoveryByUsernameAsync(string username);
        Task<UserRecoveryRecord?> GetRecoveryByEmailAsync(string email);
        Task<bool> UsernameUnavailableAsync(string username, DateTime utcNow);

        /// <summary>
        /// The subset of <paramref name="usernames"/> already held by a user or covered by an
        /// unexpired reservation. The batched form of <see cref="UsernameUnavailableAsync"/>,
        /// evaluating the same two-table predicate in one round trip.
        /// </summary>
        /// <remarks>
        /// One round trip is the contract, not an implementation detail: the point of this method
        /// is to bound what a suggestion draw costs. The two tables must therefore be composed into
        /// a single query rather than awaited one after the other — an implementation that awaits
        /// each half doubles every call and gives back half the batching.
        /// </remarks>
        Task<IReadOnlySet<string>> FindUnavailableUsernamesAsync(
            IReadOnlyCollection<string> usernames,
            DateTime utcNow,
            CancellationToken cancellationToken = default);
        Task<UserOAuthRecord?> GetOAuthByEmailAsync(string email);
        Task<UserOAuthRecord?> GetOAuthByMicrosoftIdAsync(string microsoftId);
        Task<UserOAuthRecord?> GetOAuthByGoogleIdAsync(string googleId);
        Task<UserOAuthRecord?> UpdateProviderIdsAsync(int id, string? googleId, string? microsoftId);
        Task<bool> EmailExistsAsync(string email);
        /// <summary>
        /// Swaps the account's address and bumps <c>AuthVersion</c> in one commit, so outstanding
        /// access tokens carrying the old address stop validating the moment the change lands.
        /// </summary>
        Task<EmailChangeRecord> ChangeEmailAsync(
            int userId,
            string email,
            int expectedAuthVersion,
            DateTime utcNow);
    }
}
