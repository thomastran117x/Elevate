using backend.main.features.events.images;
using backend.main.infrastructure.database.core;

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
                .ThenBy(ei => ei.Id)
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

            var now = DateTime.UtcNow;
            var entities = imageUrls.Select((url, i) => new EventImage
            {
                EventId = eventId,
                ImageUrl = url,
                SortOrder = maxSort + 1 + i,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            await _context.EventImages.AddRangeAsync(entities);
            return entities;
        }

        public async Task<EventImage> AddImageAsync(
            int eventId,
            string imageUrl,
            string? altText,
            bool isDecorative)
        {
            int maxSort = await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .MaxAsync(ei => (int?)ei.SortOrder) ?? -1;

            var now = DateTime.UtcNow;
            var entity = new EventImage
            {
                EventId = eventId,
                ImageUrl = imageUrl,
                SortOrder = maxSort + 1,
                AltText = NormalizeAltText(altText),
                IsDecorative = isDecorative,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _context.EventImages.AddAsync(entity);
            return entity;
        }

        public async Task<bool> UpdateMetadataAsync(
            int imageId,
            int eventId,
            string? altText,
            bool isDecorative)
        {
            var image = await _context.EventImages
                .FirstOrDefaultAsync(ei => ei.Id == imageId && ei.EventId == eventId);

            if (image == null)
                return false;

            image.AltText = NormalizeAltText(altText);
            image.IsDecorative = isDecorative;
            image.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        public async Task<bool> ReplaceImageUrlAsync(int imageId, int eventId, string imageUrl)
        {
            var image = await _context.EventImages
                .FirstOrDefaultAsync(ei => ei.Id == imageId && ei.EventId == eventId);

            if (image == null)
                return false;

            // Order, cover and alt text describe the slot rather than the file, so they survive.
            image.ImageUrl = imageUrl;
            image.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        public async Task SyncImagesAsync(int eventId, IReadOnlyList<string> orderedUrls)
        {
            var existing = await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .ToListAsync();

            var byUrl = new Dictionary<string, EventImage>(StringComparer.Ordinal);
            foreach (var row in existing)
                byUrl.TryAdd(row.ImageUrl, row);

            var now = DateTime.UtcNow;
            var keptIds = new HashSet<int>();
            var seenUrls = new HashSet<string>(StringComparer.Ordinal);
            var position = 0;

            foreach (var url in orderedUrls)
            {
                // A repeated URL is one image, not two: the duplicate row would be unreachable
                // and reordering could never separate the pair.
                if (!seenUrls.Add(url))
                    continue;

                if (byUrl.TryGetValue(url, out var row))
                {
                    if (row.SortOrder != position)
                    {
                        row.SortOrder = position;
                        row.UpdatedAt = now;
                    }

                    keptIds.Add(row.Id);
                }
                else
                {
                    await _context.EventImages.AddAsync(new EventImage
                    {
                        EventId = eventId,
                        ImageUrl = url,
                        SortOrder = position,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                position++;
            }

            var removed = existing.Where(row => !keptIds.Contains(row.Id)).ToList();
            if (removed.Count > 0)
                _context.EventImages.RemoveRange(removed);

            // Saved before promoting: when the removed row held the cover, the partial unique
            // index will not accept a replacement until that delete has actually landed.
            await _context.SaveChangesAsync();
            await EnsureCoverAsync(eventId);
        }

        public async Task ReorderAsync(int eventId, IReadOnlyList<int> imageIds)
        {
            var rows = await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .ToListAsync();

            var byId = rows.ToDictionary(row => row.Id);
            var now = DateTime.UtcNow;

            for (var position = 0; position < imageIds.Count; position++)
            {
                if (!byId.TryGetValue(imageIds[position], out var row))
                    continue;

                if (row.SortOrder == position)
                    continue;

                row.SortOrder = position;
                row.UpdatedAt = now;
            }
        }

        public async Task SetCoverAsync(int eventId, int imageId)
        {
            var now = DateTime.UtcNow;

            // Two statements rather than tracked edits: the partial unique index is not
            // deferrable, so the old cover must be cleared before the new one claims the slot,
            // and EF is free to order writes within a single SaveChanges however it likes.
            await _context.EventImages
                .Where(ei => ei.EventId == eventId && ei.IsCover && ei.Id != imageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ei => ei.IsCover, false)
                    .SetProperty(ei => ei.UpdatedAt, now));

            await _context.EventImages
                .Where(ei => ei.EventId == eventId && ei.Id == imageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(ei => ei.IsCover, true)
                    .SetProperty(ei => ei.UpdatedAt, now));
        }

        public async Task EnsureCoverAsync(int eventId)
        {
            var hasCover = await _context.EventImages
                .AnyAsync(ei => ei.EventId == eventId && ei.IsCover);

            if (hasCover)
                return;

            var first = await _context.EventImages
                .Where(ei => ei.EventId == eventId)
                .OrderBy(ei => ei.SortOrder)
                .ThenBy(ei => ei.Id)
                .FirstOrDefaultAsync();

            if (first == null)
                return;

            first.IsCover = true;
            first.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
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

        private static string? NormalizeAltText(string? altText) =>
            string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
    }
}
