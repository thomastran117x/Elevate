using backend.main.shared.requests;
using backend.main.shared.utilities;
using backend.main.utilities;

namespace backend.main.features.auth.device
{
    public sealed class DeviceTrustService : IDeviceTrustService
    {
        private static readonly TimeSpan TrustedDeviceLifetime = TimeSpan.FromDays(90);
        private readonly IDeviceRepository _deviceRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ClientRequestInfo _requestInfo;

        public DeviceTrustService(
            IDeviceRepository deviceRepository,
            IHttpContextAccessor httpContextAccessor,
            ClientRequestInfo requestInfo
        )
        {
            _deviceRepository = deviceRepository;
            _httpContextAccessor = httpContextAccessor;
            _requestInfo = requestInfo;
        }

        public async Task<bool> IsTrustedAsync(int userId, ClientRequestInfo requestInfo)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var trustedDeviceToken = httpContext == null
                ? null
                : HttpUtility.ResolveTrustedDeviceToken(httpContext.Request);

            if (string.IsNullOrWhiteSpace(trustedDeviceToken))
                return false;

            var device = await _deviceRepository.GetDeviceAsync(userId, ComputeDeviceTokenHash(trustedDeviceToken));
            if (device == null)
            {
                if (httpContext != null)
                    HttpUtility.ClearTrustedDeviceToken(httpContext.Response, requestInfo);

                return false;
            }

            device.IpAddress = requestInfo.IpAddress;
            device.DeviceType = requestInfo.DeviceType;
            device.ClientName = requestInfo.ClientName;
            await _deviceRepository.UpdateLastSeenAsync(device);

            if (httpContext != null)
            {
                HttpUtility.SetTrustedDeviceToken(
                    httpContext.Response,
                    requestInfo,
                    trustedDeviceToken,
                    TrustedDeviceLifetime
                );
            }

            return true;
        }

        public async Task<bool> TrustAsync(
            int userId,
            int expectedAuthVersion,
            string trustedDeviceId,
            string deviceType,
            string clientName,
            string ipAddress
        )
        {
            var trusted = await _deviceRepository.TrustIfAuthVersionAsync(
                userId,
                expectedAuthVersion,
                new Device
                {
                    UserId = userId,
                    DeviceTokenHash = ComputeDeviceTokenHash(trustedDeviceId),
                    DeviceType = deviceType,
                    ClientName = clientName,
                    IpAddress = ipAddress
                });
            if (!trusted)
                return false;

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                HttpUtility.SetTrustedDeviceToken(
                    httpContext.Response,
                    _requestInfo,
                    trustedDeviceId,
                    TrustedDeviceLifetime
                );
            }

            return true;
        }

        private static string ComputeDeviceTokenHash(string deviceToken) => CryptoHelper.HashToken(deviceToken);
    }
}
