using backend.main.features.events.registration;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.registration
{
    public class EventRegistrationRepository : IEventRegistrationRepository
    {
        private readonly AppDatabaseContext _context;

        public EventRegistrationRepository(AppDatabaseContext context) => _context = context;

        public async Task<EventRegistration?> IsRegisteredAsync(int eventId, int userId)
        {
            return await _context.EventRegistrations
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.EventId == eventId &&
                    r.UserId == userId &&
                    r.Status == RegistrationStatus.Active);
        }

        public async Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await _context.EventRegistrations
                .AsNoTracking()
                .Where(r => r.EventId == eventId && r.Status == RegistrationStatus.Active)
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
                .Where(r => r.UserId == userId && r.Status == RegistrationStatus.Active)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }
    }
}
