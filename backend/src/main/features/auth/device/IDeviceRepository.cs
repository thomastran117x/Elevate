using backend.main.features.profile;

namespace backend.main.features.auth.device
{
    public interface IDeviceRepository
    {
        Task<Device?> GetDeviceAsync(int userId, string deviceTokenHash);

        /// <summary>
        /// Creates the trusted-device row, or refreshes it if it already exists, but only while
        /// the account is still at <paramref name="expectedAuthVersion"/>. Returns false and
        /// writes nothing when the account is gone or its version has moved on.
        /// </summary>
        Task<bool> TrustIfAuthVersionAsync(int userId, int expectedAuthVersion, Device device);

        Task UpdateLastSeenAsync(Device device);
    }
}

