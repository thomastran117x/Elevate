using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface IOAuthService
    {
        Task<string> ExchangeGoogleCodeAsync(string code, string codeVerifier, string redirectUri);
        Task<OAuthUser> VerifyGoogleTokenAsync(string googleToken, string? expectedNonce = null);
        Task<OAuthUser> VerifyMicrosoftTokenAsync(
            string microsoftToken,
            string? expectedNonce = null
        );
        Task<OAuthUser> VerifyAppleTokenAsync(string appleToken);
    }
}
