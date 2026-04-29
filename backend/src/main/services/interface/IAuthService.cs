using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface IAuthService
    {
        Task<UserToken> LoginAsync(string email, string password, bool rememberMe = false);
        Task<VerificationOtpChallenge> SignUpAsync(string email, string password, string usertype);
        Task<UserToken> VerifyAsync(string token);
        Task<UserToken> VerifyOtpAsync(string code, string challenge);
        Task<UserToken> VerifyDeviceLoginAsync(string token);
        Task<UserToken> GoogleAsync(string token, string? expectedNonce = null);
        Task<UserToken> MicrosoftAsync(string token);
        Task<UserToken> HandleTokensAsync(string refreshToken);
        Task HandleLogoutAsync(string refreshToken);
        Task<VerificationOtpChallenge> ForgotPasswordAsync(string email);
        Task ChangePasswordAsync(string token, string password);
        Task ChangePasswordWithOtpAsync(string code, string challenge, string password);
    }
}
