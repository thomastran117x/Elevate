using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.attributes.repository;

namespace backend.main.features.profile
{
    public interface IUserRepository
    {
        Task<User?> UpdateUserAsync(int id, User updated);
        Task<User?> UpdatePartialAsync(User user);
        /// <summary>
        /// Writes only the avatar column, returning the reloaded user together with the URL that
        /// write replaced, or null when the account does not exist.
        /// </summary>
        /// <remarks>
        /// The caller owns blob cleanup, and which blob depends on how this ends:
        /// <list type="bullet">
        /// <item>
        /// It returns a record: delete <c>PreviousAvatar</c>, the URL this swap replaced.
        /// </item>
        /// <item>
        /// It returns null, or throws <see cref="shared.exceptions.http.ConflictException"/>
        /// because every attempt lost its race: nothing was written, so delete the blob just
        /// uploaded.
        /// </item>
        /// <item>
        /// It throws <see cref="AvatarSwapSupersededException"/>: delete the blob just uploaded
        /// <em>and</em> that exception's <c>ReplacedAvatarUrl</c>. Nothing else will ever report
        /// that second URL, and the orphan sweeper is opt-in, so skipping it leaves the blob in a
        /// public container permanently.
        /// </item>
        /// </list>
        /// <para>
        /// Deliberately not <see cref="UpdatePartialAsync"/>: that copies Name, Address and Phone
        /// from the caller's <see cref="User"/>, and the avatar flow holds that copy across image
        /// decoding, so it would revert any profile edit saved in the meantime.
        /// </para>
        /// <para>
        /// <see cref="NoRetryAttribute"/> because this method already retries internally, through
        /// the execution strategy for transient faults and its own loop for lost races. Re-running
        /// the whole method on top of that would read the avatar it just wrote as the value it
        /// replaced, and report that instead of the real predecessor.
        /// </para>
        /// </remarks>
        [NoRetry]
        Task<AvatarSwapRecord?> SwapAvatarAsync(int id, string avatarUrl);
        Task<bool> UsernameExistsAsync(string username, int excludeUserId);
        /// <summary>
        /// Deletes the user and returns the blob URLs (avatar plus cascade-deleted club,
        /// club-version and event images) that are now orphaned and should be cleaned up.
        /// Returns an empty list when the user does not exist.
        /// </summary>
        Task<IReadOnlyList<string>> DeleteUserAsync(int id);
        /// <summary>
        /// Returns a sanitized User aggregate for non-auth workflows. Password is always null.
        /// </summary>
        Task<User?> GetUserAsync(int id);
        /// <summary>
        /// Whether the account exists, without projecting the row. For the callers that only need
        /// to refuse work early, such as an avatar upload whose token may outlive its account.
        /// </summary>
        Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
        Task<UserProfileRecord?> GetProfileByUsernameAsync(string username);
        Task<UserProfileRecord?> GetPublicProfileByUsernameOrReservationAsync(
            string username,
            DateTime utcNow
        );
        Task<UsernameChangeRecord> ChangeUsernameAsync(
            int userId,
            string username,
            string usernameDisplay,
            DateTime utcNow,
            DateTime reservedUntilUtc
        );
        Task<UserProfileRecord?> GetProfileByEmailAsync(string email);
        Task<IReadOnlyList<UserListRecord>> GetUsersAsync(
            string? role = null,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        );
        Task<IReadOnlyList<UserListRecord>> GetByIdsAsync(
            IEnumerable<int> ids,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        );
    }
}
