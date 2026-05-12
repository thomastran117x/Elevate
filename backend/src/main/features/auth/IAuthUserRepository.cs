using backend.main.features.auth.contracts;
using backend.main.models.core;

namespace backend.main.features.auth
{
    public interface IAuthUserRepository
    {
        Task<User> CreateUserAsync(User user);
        Task<User?> UpdateUserAsync(int id, User updated);
        Task<UserStatusRecord?> UpdateUserStatusAsync(int id, bool isDisabled, string? disabledReason);
        Task<bool> IncrementAuthVersionAsync(int id);
        Task<User?> GetUserAsync(int id);
        Task<UserAuthRecord?> GetAuthByEmailAsync(string email);
        Task<UserOAuthRecord?> GetOAuthByEmailAsync(string email);
        Task<UserOAuthRecord?> GetOAuthByMicrosoftIdAsync(string microsoftId);
        Task<UserOAuthRecord?> GetOAuthByGoogleIdAsync(string googleId);
        Task<UserOAuthRecord?> UpdateProviderIdsAsync(int id, string? googleId, string? microsoftId);
        Task<bool> EmailExistsAsync(string email);
    }
}
