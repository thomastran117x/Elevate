using backend.main.models.core;

namespace backend.main.repositories.interfaces
{
    public interface IDeviceRepository
    {
        Task<Device?> GetDeviceAsync(int userId, string deviceType, string clientName);
        Task<Device> CreateDeviceAsync(Device device);
        Task UpdateLastSeenAsync(Device device);
    }
}
