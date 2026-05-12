using backend.main.features.profile;

namespace backend.main.features.auth.device
{
    public interface IDeviceRepository
    {
        Task<Device?> GetDeviceAsync(int userId, string deviceTokenHash);
        Task<Device> CreateDeviceAsync(Device device);
        Task UpdateLastSeenAsync(Device device);
    }
}

