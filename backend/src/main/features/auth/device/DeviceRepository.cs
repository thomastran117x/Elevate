using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.auth.device
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

        public async Task<bool> TrustIfAuthVersionAsync(int userId, int expectedAuthVersion, Device device)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Every AuthVersion bump writes this row, so holding its lock until the device
                    // is committed leaves a rotation two outcomes: it committed first and the
                    // comparison below sees it, or it waits until the trust exists and then sweeps
                    // past it like any device trusted before the change. Comparing first and
                    // writing afterwards, even immediately, would leave a gap between the two that
                    // a rotation could commit into, granting trust to a sign-in it had rejected.
                    var user = _context.Database.IsNpgsql()
                        ? await _context.Users
                            .FromSqlInterpolated(
                                $"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                            .AsNoTracking()
                            .SingleOrDefaultAsync()
                        : await _context.Users
                            .AsNoTracking()
                            .FirstOrDefaultAsync(u => u.Id == userId);
                    if (user == null || user.AuthVersion != expectedAuthVersion)
                        return false;

                    var existing = await _context.Devices
                        .FirstOrDefaultAsync(d =>
                            d.UserId == userId &&
                            d.DeviceTokenHash == device.DeviceTokenHash);
                    if (existing == null)
                    {
                        device.UserId = userId;
                        await _context.Devices.AddAsync(device);
                    }
                    else
                    {
                        // A completed verification renews the trust; only passive use does not.
                        existing.LastSeenAt = DateTime.UtcNow;
                        existing.ExpiresAt = device.ExpiresAt;
                        existing.IpAddress = device.IpAddress;
                        existing.DeviceType = device.DeviceType;
                        existing.ClientName = device.ClientName;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    throw;
                }
            });
        }

        public async Task<List<Device>> ListActiveAsync(int userId, DateTime now)
        {
            return await _context.Devices
                .AsNoTracking()
                .Where(d => d.UserId == userId && d.ExpiresAt > now)
                .OrderByDescending(d => d.LastSeenAt)
                .ThenBy(d => d.Id)
                .ToListAsync();
        }

        public async Task<bool> DeleteAsync(int userId, int deviceId)
        {
            return await _context.Devices
                .Where(d => d.Id == deviceId && d.UserId == userId)
                .ExecuteDeleteAsync() > 0;
        }

        public async Task<int> DeleteAllAsync(int userId)
        {
            return await _context.Devices
                .Where(d => d.UserId == userId)
                .ExecuteDeleteAsync();
        }

        public async Task<int> DeleteExpiredAsync(DateTime now, int batchSize)
        {
            var ids = await _context.Devices
                .AsNoTracking()
                .Where(d => d.ExpiresAt <= now)
                .OrderBy(d => d.ExpiresAt)
                .Take(batchSize)
                .Select(d => d.Id)
                .ToListAsync();
            if (ids.Count == 0)
                return 0;

            return await _context.Devices
                .Where(d => ids.Contains(d.Id))
                .ExecuteDeleteAsync();
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

