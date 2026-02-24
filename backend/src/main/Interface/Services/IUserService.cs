using backend.main.Models;

namespace backend.main.Interfaces
{
    public interface IUserService
    {
        Task<List<User>> GetAllUsersAsync();
        Task<User> GetUserByIdAsync(int id);
        Task<User?> UpdateUserAsync(int id, User updatedUser);
        Task<User?> UpdateAvatarAsync(int id, IFormFile image);
        Task<bool> DeleteUserAsync(int id);
        Task<IEnumerable<FollowClub>> GetUserFollowingsAsync(int id, int page = 1, int pageSize = 20);
    }
}
