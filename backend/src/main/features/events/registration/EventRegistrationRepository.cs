using System.Data;

using backend.main.infrastructure.database.core;
using backend.main.features.events.registration;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.registration
{
    public class EventRegistrationRepository : IEventRegistrationRepository
    {
        private readonly AppDatabaseContext _context;

        public EventRegistrationRepository(AppDatabaseContext context) => _context = context;

        public async Task<EventRegistration?> TryRegisterAsync(int eventId, int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                // Re-read the event row inside the SERIALIZABLE transaction.
                // This acquires a shared lock on the row, preventing a concurrent organizer
                // UPDATE (e.g. closing the event) from slipping between the service-layer
                // cache read and this insert.
                var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == eventId);

                if (ev == null)
                {
                    await transaction.RollbackAsync();
                    return null;
                }

                if (ev.EndTime.HasValue && ev.EndTime <= DateTime.UtcNow)
                {
                    await transaction.RollbackAsync();
                    return null;
                }

                var count = await _context.EventRegistrations
                    .CountAsync(r => r.EventId == eventId);

                if (count >= ev.maxParticipants)
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
                ev.RegistrationCount += 1;
                ev.UpdatedAt = DateTime.UtcNow;
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
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var registration = await _context.EventRegistrations
                    .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);

                if (registration == null)
                {
                    await transaction.RollbackAsync();
                    return false;
                }

                _context.EventRegistrations.Remove(registration);

                var ev = await _context.Events.FirstOrDefaultAsync(e => e.Id == eventId);
                if (ev != null)
                {
                    ev.RegistrationCount = Math.Max(0, ev.RegistrationCount - 1);
                    ev.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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


