using backend.main.features.auth.events;
using backend.main.shared.requests;
using backend.main.shared.utilities;
using backend.main.utilities;

namespace backend.main.features.auth.device
{
    public sealed class DeviceTrustService : IDeviceTrustService
    {
        private readonly IDeviceRepository _deviceRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ClientRequestInfo _requestInfo;
        private readonly TimeProvider _timeProvider;
        private readonly IAuthSecurityEventEmitter _securityEvents;

        public DeviceTrustService(
            IDeviceRepository deviceRepository,
            IHttpContextAccessor httpContextAccessor,
            ClientRequestInfo requestInfo,
            TimeProvider? timeProvider = null,
            IAuthSecurityEventEmitter? securityEvents = null
        )
        {
            _deviceRepository = deviceRepository;
            _httpContextAccessor = httpContextAccessor;
            _requestInfo = requestInfo;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _securityEvents = securityEvents ?? NullAuthSecurityEventEmitter.Instance;
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
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (device == null || device.ExpiresAt <= now)
            {
                // The expiry is checked here, not left to the cookie, which the client controls. A
                // stale cookie is cleared and its row removed, so verification is asked for again.
                if (device != null && await _deviceRepository.DeleteAsync(userId, device.Id))
                {
                    _securityEvents.Emit(new AuthSecurityEvent
                    {
                        Definition = AuthSecurityEvents.TrustedDeviceExpired,
                        Outcome = AuthSecurityOutcome.Succeeded,
                        UserId = userId,
                    });
                }

                if (httpContext != null)
                    HttpUtility.ClearTrustedDeviceToken(httpContext.Response, requestInfo);

                return false;
            }

            device.IpAddress = requestInfo.IpAddress;
            device.DeviceType = requestInfo.DeviceType;
            device.ClientName = requestInfo.ClientName;
            await _deviceRepository.UpdateLastSeenAsync(device);

            // Sent again so a client that lost it gets it back, but only for the time the trust has
            // left: signing in on a trusted device does not extend it.
            if (httpContext != null)
            {
                HttpUtility.SetTrustedDeviceToken(
                    httpContext.Response,
                    requestInfo,
                    trustedDeviceToken,
                    device.ExpiresAt - now
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
                    IpAddress = ipAddress,
                    ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime + Device.TrustLifetime,
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
                    Device.TrustLifetime
                );
            }

            return true;
        }

        private static string ComputeDeviceTokenHash(string deviceToken) => CryptoHelper.HashToken(deviceToken);
    }
}
