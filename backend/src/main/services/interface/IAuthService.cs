using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface IAuthService
    {
        Task<UserToken> LoginAsync(string email, string password);
        Task SignUpAsync(string email, string password, string usertype);
        Task<UserToken> VerifyAsync(string token);
        Task<UserToken> VerifyDeviceLoginAsync(string token);
        Task<UserToken> GoogleAsync(string token);
        Task<UserToken> MicrosoftAsync(string token);
        Task<UserToken> HandleTokensAsync(string refreshToken);
        Task HandleLogoutAsync(string refreshToken);
        Task ForgotPasswordAsync(string email);
        Task ChangePasswordAsync(string token, string password);
    }
}
