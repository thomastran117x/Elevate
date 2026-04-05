using System.Data;

using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class EventRegistrationRepository : IEventRegistrationRepository
    {
        private readonly AppDatabaseContext _context;

        public EventRegistrationRepository(AppDatabaseContext context) => _context = context;

        public async Task<EventRegistration?> TryRegisterAsync(int eventId, int userId, int maxParticipants)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var count = await _context.EventRegistrations
                    .CountAsync(r => r.EventId == eventId);

                if (count >= maxParticipants)
                {
                    await transaction.RollbackAsync();
                    return null;
                }

                var registration = new EventRegistration
                {
                    EventId = eventId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.EventRegistrations.Add(registration);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return registration;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> UnregisterAsync(int eventId, int userId)
        {
            var registration = await _context.EventRegistrations
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);

            if (registration == null)
                return false;

            _context.EventRegistrations.Remove(registration);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<EventRegistration?> IsRegisteredAsync(int eventId, int userId)
        {
            return await _context.EventRegistrations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);
        }

        public async Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.EventId == eventId)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}
