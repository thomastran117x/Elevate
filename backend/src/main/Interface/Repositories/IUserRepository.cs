using backend.main.Models;

namespace backend.main.Interfaces
{
    public interface IUserRepository
    {
        Task<User> CreateUserAsync(User user);
        Task<User?> UpdateUserAsync(int id, User updated);
        Task<User?> UpdatePartialAsync(User user);
        Task<User?> UpdateProviderIdsAsync(int id, string? googleId, string? microsoftId);
        Task<bool> DeleteUserAsync(int id);
        Task<User?> GetUserAsync(int id);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByMicrosoftIdAsync(string microsoftId);
        Task<User?> GetUserByGoogleIdAsync(string googleId);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<IEnumerable<User>> GetUsersAsync(string? role = null);
        Task<bool> EmailExistsAsync(string email);
        Task<List<User>> GetByIdsAsync(IEnumerable<int> ids);
    }
}
