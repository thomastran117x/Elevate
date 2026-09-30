using backend.main.features.auth.contracts;
using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.features.profile.contracts;

namespace backend.main.features.profile
{
    public interface IUserService
    {
        Task<IReadOnlyList<UserListRecord>> GetAllUsersAsync(
            string? role = null,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        );
        Task<User> GetUserByIdAsync(int id);
        Task<UserProfileRecord> GetPublicProfileByUsernameAsync(string username);
        Task<User?> UpdateUserAsync(int id, User updatedUser);
        Task<User> ChangeUsernameAsync(int id, string username);
        /// <summary>
        /// Replaces the avatar, returning the updated user. Never returns null: a missing account
        /// raises <see cref="shared.exceptions.http.ResourceNotFoundException"/>.
        /// </summary>
        Task<User> UpdateAvatarAsync(int id, IFormFile image, CancellationToken cancellationToken = default);
        Task<bool> DeleteUserAsync(int id);
        Task<UserStatusRecord> UpdateUserStatusAsync(int id, bool isDisabled, string? reason);
        Task<IEnumerable<FollowClub>> GetUserFollowingsAsync(int id, int page = 1, int pageSize = 20);
    }
}
