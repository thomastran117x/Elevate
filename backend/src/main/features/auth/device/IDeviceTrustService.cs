using backend.main.shared.requests;

namespace backend.main.features.auth.device
{
    public interface IDeviceTrustService
    {
        Task<bool> IsTrustedAsync(int userId, ClientRequestInfo requestInfo);
        Task TrustAsync(
            int userId,
            string trustedDeviceId,
            string deviceType,
            string clientName,
            string ipAddress
        );
    }
}
