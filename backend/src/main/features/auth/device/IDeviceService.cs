using backend.main.features.auth.token;
using backend.main.shared.requests;

namespace backend.main.features.auth.device
{
    public interface IDeviceService
    {
        Task EnsureDeviceKnownAsync(int userId, string userEmail, ClientRequestInfo requestInfo, string? returnUrl = null);
        Task<AuthenticatedSessionResult> VerifyDeviceAsync(string token, SessionTransport transport);
    }
}
