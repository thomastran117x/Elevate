using backend.main.models.core;
using backend.main.features.auth.contracts;
using backend.main.repositories.contracts.users;

namespace backend.main.services.interfaces
{
    public interface IUserService
    {
        Task<IReadOnlyList<UserListRecord>> GetAllUsersAsync(
            string? role = null,
            UserReadDetailLevel detail = UserReadDetailLevel.Slim
        );
        Task<User> GetUserByIdAsync(int id);
        Task<User?> UpdateUserAsync(int id, User updatedUser);
        Task<User?> UpdateAvatarAsync(int id, IFormFile image);
        Task<bool> DeleteUserAsync(int id);
        Task<UserStatusRecord> UpdateUserStatusAsync(int id, bool isDisabled, string? reason);
        Task<IEnumerable<FollowClub>> GetUserFollowingsAsync(int id, int page = 1, int pageSize = 20);
    }
}
