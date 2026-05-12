using backend.main.features.profile;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;

namespace backend.main.features.auth
{
    public interface IAuthService
    {
        Task<UserToken> LoginAsync(
            string email,
            string password,
            SessionTransport transport,
            bool rememberMe = false
        );
        Task<VerificationOtpChallenge> SignUpAsync(string email, string password, string usertype);
        Task<UserToken> VerifyAsync(string token, SessionTransport transport);
        Task<UserToken> VerifyOtpAsync(string code, string challenge, SessionTransport transport);
        Task<UserToken> VerifyDeviceLoginAsync(string token, SessionTransport transport);
        Task<OAuthAuthenticationResult> GoogleAsync(
            string token,
            SessionTransport transport,
            string? expectedNonce = null
        );
        Task<OAuthAuthenticationResult> GoogleCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri,
            SessionTransport transport,
            string? nonce = null
        );
        Task<OAuthAuthenticationResult> MicrosoftAsync(
            string token,
            SessionTransport transport,
            string? expectedNonce = null
        );
        Task<UserToken> CompleteOAuthSignupAsync(
            string signupToken,
            string usertype,
            SessionTransport transport
        );
        Task<User> GetCurrentUserAsync(int userId);
        Task<UserToken> HandleTokensAsync(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport transport
        );
        Task HandleLogoutAsync(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport transport
        );
        Task<VerificationOtpChallenge> ForgotPasswordAsync(string email);
        Task ChangePasswordAsync(string token, string password);
        Task ChangePasswordWithOtpAsync(string code, string challenge, string password);
    }
}

