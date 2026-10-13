using backend.main.shared.requests;

namespace backend.main.features.auth.device
{
    public interface IDeviceTrustService
    {
        Task<bool> IsTrustedAsync(int userId, ClientRequestInfo requestInfo);

        /// <summary>
        /// Trusts the device for the account, provided the account is still at
        /// <paramref name="expectedAuthVersion"/> when the trust is written. Returns false, without
        /// trusting the device or setting its cookie, when the version has moved on.
        /// </summary>
        Task<bool> TrustAsync(
            int userId,
            int expectedAuthVersion,
            string trustedDeviceId,
            string deviceType,
            string clientName,
            string ipAddress
        );
    }
}
