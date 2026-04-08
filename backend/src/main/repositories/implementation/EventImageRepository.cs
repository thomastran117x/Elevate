using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class EventImageRepository : IEventImageRepository
    {
        private readonly AppDatabaseContext _context;

        public EventImageRepository(AppDatabaseContext context) => _context = context;

        public async Task<List<EventImage>> GetByEventIdAsync(int eventId)
        {
            return await _context.EventImages
                .AsNoTracking()
                .Where(ei => ei.EventId == eventId)
                .OrderBy(ei => ei.SortOrder)
                .ToListAsync();
        }

        public async Task<EventImage?> GetByIdAsync(int id, int eventId)
        {
            return await _context.EventImages
                .AsNoTracking()
                .FirstOrDefaultAsync(ei => ei.Id == id && ei.EventId == eventId);
        }

        public async Task<List<EventImage>> AddImagesAsync(int eventId, IEnumerable<string> imageUrls)
        {
            int maxSort = await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .MaxAsync(ei => (int?)ei.SortOrder) ?? -1;

            var entities = imageUrls.Select((url, i) => new EventImage
            {
                EventId = eventId,
                ImageUrl = url,
                SortOrder = maxSort + 1 + i,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await _context.EventImages.AddRangeAsync(entities);
            await _context.SaveChangesAsync();
            return entities;
        }

        public async Task<bool> DeleteImageAsync(int imageId, int eventId)
        {
            var deleted = await _context.EventImages
                .Where(ei => ei.Id == imageId && ei.EventId == eventId)
                .ExecuteDeleteAsync();
            return deleted > 0;
        }

        public async Task DeleteAllByEventIdAsync(int eventId)
        {
            await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .ExecuteDeleteAsync();
        }

        public async Task<int> CountByEventIdAsync(int eventId)
        {
            return await _context.EventImages
                .CountAsync(ei => ei.EventId == eventId);
        }
    }
}
