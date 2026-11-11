using System.Net;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities;

namespace backend.main.features.auth.device
{
    public sealed class TrustedDeviceManagementService : ITrustedDeviceManagementService
    {
        private readonly IDeviceRepository _devices;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private readonly TimeProvider _timeProvider;

        public TrustedDeviceManagementService(
            IDeviceRepository devices,
            IAuthSecurityEventEmitter securityEvents,
            TimeProvider timeProvider)
        {
            _devices = devices;
            _securityEvents = securityEvents;
            _timeProvider = timeProvider;
        }

        public async Task<TrustedDevicesResponse> ListAsync(int userId, string? currentDeviceToken)
        {
            var devices = await _devices.ListActiveAsync(userId, _timeProvider.GetUtcNow().UtcDateTime);
            var currentHash = HashOf(currentDeviceToken);

            return new TrustedDevicesResponse
            {
                Devices = devices.Select(device => ToResponse(device, currentHash)).ToList(),
            };
        }

        public async Task<TrustedDeviceRevocationResponse> RevokeAsync(
            int userId,
            int deviceId,
            string? currentDeviceToken)
        {
            // Read first only to say whether it was the current device. The delete itself is
            // filtered by owner, so another account's id is refused the same way as an unknown one.
            var currentHash = HashOf(currentDeviceToken);
            var isCurrent = currentHash is not null
                && await _devices.GetDeviceAsync(userId, currentHash) is { } current
                && current.Id == deviceId;

            if (!await _devices.DeleteAsync(userId, deviceId))
                throw new ResourceNotFoundException("Trusted device not found.");

            Emit(userId, AuthSecurityReason.UserRevokedTrustedDevice);
            return new TrustedDeviceRevocationResponse
            {
                RevokedCount = 1,
                CurrentDeviceRevoked = isCurrent,
            };
        }

        public async Task<TrustedDeviceRevocationResponse> RevokeAllAsync(int userId, string? currentDeviceToken)
        {
            var currentHash = HashOf(currentDeviceToken);
            var hadCurrent = currentHash is not null && await _devices.GetDeviceAsync(userId, currentHash) is not null;

            var revoked = await _devices.DeleteAllAsync(userId);
            if (revoked > 0)
                Emit(userId, AuthSecurityReason.UserRevokedAllTrustedDevices);

            return new TrustedDeviceRevocationResponse
            {
                RevokedCount = revoked,
                CurrentDeviceRevoked = hadCurrent,
            };
        }

        private void Emit(int userId, AuthSecurityReason reason) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.TrustedDeviceRevoked,
                Outcome = AuthSecurityOutcome.Succeeded,
                Reason = reason,
                UserId = userId,
            });

        private static string? HashOf(string? deviceToken) =>
            string.IsNullOrWhiteSpace(deviceToken) ? null : CryptoHelper.HashToken(deviceToken);

        /// <remarks>
        /// The stored client name can hold free text from the User-Agent and the address is stored
        /// whole, so both are reduced to what the security history shows.
        /// </remarks>
        private static TrustedDeviceResponse ToResponse(Device device, string? currentHash) => new()
        {
            Id = device.Id,
            Current = currentHash is not null && device.DeviceTokenHash == currentHash,
            Client = AuthSecurityWireNames.Of(SecurityHistoryClients.FromClientName(device.ClientName)),
            DeviceClass = AuthSecurityWireNames.Of(AuthDeviceClasses.FromDeviceType(device.DeviceType)),
            Network = IPAddress.TryParse(device.IpAddress, out var address)
                ? NetworkAddressMasker.Mask(address)
                : null,
            CreatedAt = device.CreatedAt,
            LastSeenAt = device.LastSeenAt,
            ExpiresAt = device.ExpiresAt,
        };
    }
}
