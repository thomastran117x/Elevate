using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class DeviceRepository : BaseRepository, IDeviceRepository
    {
        public DeviceRepository(AppDatabaseContext context)
            : base(context) { }

        public async Task<Device?> GetDeviceAsync(int userId, string deviceType, string clientName)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Devices
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d =>
                        d.UserId == userId &&
                        d.DeviceType == deviceType &&
                        d.ClientName == clientName);
            });
        }

        public async Task<Device> CreateDeviceAsync(Device device)
        {
            return await ExecuteAsync(async () =>
            {
                await _context.Devices.AddAsync(device);
                await _context.SaveChangesAsync();
                return device;
            });
        }

        public async Task UpdateLastSeenAsync(Device device)
        {
            await ExecuteAsync(async () =>
            {
                var existing = await _context.Devices.FindAsync(device.Id);
                if (existing == null)
                    return;

                existing.LastSeenAt = DateTime.UtcNow;
                existing.IpAddress = device.IpAddress;
                await _context.SaveChangesAsync();
            });
        }
    }
}
