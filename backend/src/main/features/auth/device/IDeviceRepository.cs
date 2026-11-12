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

        /// <summary>The account's unexpired trusted devices, most recently used first.</summary>
        Task<List<Device>> ListActiveAsync(int userId, DateTime now);

        /// <summary>
        /// Removes one of <paramref name="userId"/>'s devices. Another account's device is left
        /// alone and reported the same way as an unknown id.
        /// </summary>
        Task<bool> DeleteAsync(int userId, int deviceId);

        /// <returns>How many devices were removed.</returns>
        Task<int> DeleteAllAsync(int userId);

        /// <summary>Removes up to <paramref name="batchSize"/> devices whose trust has ended.</summary>
        /// <returns>How many devices were removed.</returns>
        Task<int> DeleteExpiredAsync(DateTime now, int batchSize);
    }
}

