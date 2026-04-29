using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface IOAuthService
    {
        Task<OAuthUser> VerifyGoogleTokenAsync(string googleToken, string? expectedNonce = null);
        Task<OAuthUser> VerifyMicrosoftTokenAsync(string microsoftToken);
        Task<OAuthUser> VerifyAppleTokenAsync(string appleToken);
    }
}
