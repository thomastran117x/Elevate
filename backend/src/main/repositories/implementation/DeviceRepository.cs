using backend.main.infrastructure.database.core;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class DeviceRepository : IDeviceRepository
    {
        private readonly AppDatabaseContext _context;

        public DeviceRepository(AppDatabaseContext context) => _context = context;

        public async Task<Device?> GetDeviceAsync(int userId, string deviceTokenHash)
        {
            return await _context.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.UserId == userId &&
                    d.DeviceTokenHash == deviceTokenHash);
        }

        public async Task<Device> CreateDeviceAsync(Device device)
        {
            await _context.Devices.AddAsync(device);
            await _context.SaveChangesAsync();
            return device;
        }

        public async Task UpdateLastSeenAsync(Device device)
        {
            var existing = await _context.Devices.FindAsync(device.Id);
            if (existing == null)
                return;

            existing.LastSeenAt = DateTime.UtcNow;
            existing.IpAddress = device.IpAddress;
            existing.DeviceType = device.DeviceType;
            existing.ClientName = device.ClientName;
            await _context.SaveChangesAsync();
        }
    }
}
