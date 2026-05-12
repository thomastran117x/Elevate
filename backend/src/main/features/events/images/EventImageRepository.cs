using backend.main.infrastructure.database.core;
using backend.main.features.events.images;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.images
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
            return entities;
        }

        public async Task<bool> DeleteImageAsync(int imageId, int eventId)
        {
            var image = await _context.EventImages
                .FirstOrDefaultAsync(ei => ei.Id == imageId && ei.EventId == eventId);

            if (image == null)
                return false;

            _context.EventImages.Remove(image);
            return true;
        }

        public async Task DeleteAllByEventIdAsync(int eventId)
        {
            var images = await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .ToListAsync();

            if (images.Count > 0)
                _context.EventImages.RemoveRange(images);
        }

        public async Task<int> CountByEventIdAsync(int eventId)
        {
            return await _context.EventImages
                .CountAsync(ei => ei.EventId == eventId);
        }
    }
}


