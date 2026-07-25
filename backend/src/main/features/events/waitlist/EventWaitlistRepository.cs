using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.waitlist
{
    public class EventWaitlistRepository : IEventWaitlistRepository
    {
        private readonly AppDatabaseContext _context;

        public EventWaitlistRepository(AppDatabaseContext context) => _context = context;

        public async Task<EventWaitlistEntry?> GetEntryAsync(int eventId, int userId)
        {
            return await _context.EventWaitlistEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.EventId == eventId && w.UserId == userId);
        }

        public async Task<EventWaitlistEntry?> GetEntryByIdAsync(int eventId, int entryId)
        {
            return await _context.EventWaitlistEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.EventId == eventId && w.Id == entryId);
        }

        public async Task<IReadOnlyList<EventWaitlistEntry>> GetWaitingByEventAsync(int eventId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await _context.EventWaitlistEntries
                .AsNoTracking()
                .Where(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Waiting)
                .OrderBy(w => w.JoinedAtUtc)
                .ThenBy(w => w.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountWaitingAsync(int eventId)
        {
            return await _context.EventWaitlistEntries
                .AsNoTracking()
                .CountAsync(w => w.EventId == eventId && w.Status == EventWaitlistEntryStatus.Waiting);
        }

        public async Task<int> GetPositionAsync(int eventId, DateTime joinedAtUtc, int entryId)
        {
            // Tie-break on Id: two rows can share a datetime(6) value, and a position must be
            // a total order.
            var ahead = await _context.EventWaitlistEntries
                .AsNoTracking()
                .CountAsync(w =>
                    w.EventId == eventId &&
                    w.Status == EventWaitlistEntryStatus.Waiting &&
                    (w.JoinedAtUtc < joinedAtUtc ||
                        (w.JoinedAtUtc == joinedAtUtc && w.Id < entryId)));

            return ahead + 1;
        }

        public async Task<IReadOnlyList<EventWaitlistEntry>> GetWaitingByUserAsync(int userId)
        {
            return await _context.EventWaitlistEntries
                .AsNoTracking()
                .Where(w => w.UserId == userId && w.Status == EventWaitlistEntryStatus.Waiting)
                .OrderBy(w => w.JoinedAtUtc)
                .ThenBy(w => w.Id)
                .ToListAsync();
        }
    }
}
