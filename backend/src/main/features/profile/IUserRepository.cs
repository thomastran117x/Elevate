using backend.main.features.profile;
using backend.main.features.profile.contracts;

namespace backend.main.features.profile
{
    public interface IUserRepository
    {
        Task<User?> UpdateUserAsync(int id, User updated);
        Task<User?> UpdatePartialAsync(User user);
        /// <summary>
        /// Writes only the avatar column and returns the URL it replaced, or null when the user
        /// does not exist.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="UpdatePartialAsync"/>: that copies Name, Address and Phone
        /// from the caller's <see cref="User"/>, and the avatar flow holds that copy across image
        /// decoding, so it would revert any profile edit saved in the meantime.
        /// </remarks>
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
