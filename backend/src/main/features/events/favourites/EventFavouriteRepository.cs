using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.favourites
{
    public class EventFavouriteRepository : IEventFavouriteRepository
    {
        private readonly AppDatabaseContext _context;

        public EventFavouriteRepository(AppDatabaseContext context) => _context = context;

        public async Task<IReadOnlyList<int>> GetEventIdsByUserAsync(int userId)
        {
            return await _context.EventFavourites
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .OrderBy(f => f.EventId)
                .Select(f => f.EventId)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<EventFavourite>> GetByUserAsync(int userId)
        {
            return await _context.EventFavourites
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .OrderBy(f => f.CreatedAt)
                .ThenBy(f => f.Id)
                .ToListAsync();
        }

        public async Task<bool> ExistsAsync(int eventId, int userId)
        {
            return await _context.EventFavourites
                .AsNoTracking()
                .AnyAsync(f => f.EventId == eventId && f.UserId == userId);
        }
    }
}
