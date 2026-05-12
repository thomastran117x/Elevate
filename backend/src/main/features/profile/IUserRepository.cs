using backend.main.features.profile.contracts;
using backend.main.features.profile;

namespace backend.main.features.profile
{
    public interface IUserRepository
    {
        Task<User?> UpdateUserAsync(int id, User updated);
        Task<User?> UpdatePartialAsync(User user);
        Task<bool> DeleteUserAsync(int id);
        /// <summary>
        /// Returns a sanitized User aggregate for non-auth workflows. Password is always null.
        /// </summary>
        Task<User?> GetUserAsync(int id);
        Task<UserProfileRecord?> GetProfileByUsernameAsync(string username);
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

