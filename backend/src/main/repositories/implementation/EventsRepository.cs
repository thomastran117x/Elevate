using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.models.search;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class EventsRepository : IEventsRepository
    {
        private readonly AppDatabaseContext _context;

        public EventsRepository(AppDatabaseContext context) => _context = context;

        public async Task<Events> CreateAsync(Events events)
        {
            _context.Events.Add(events);
            await _context.SaveChangesAsync();
            return events;
        }

        public async Task<Events?> GetByIdAsync(int id)
        {
            return await _context.Events
                .Include(e => e.Images)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task<IEnumerable<Events>> GetAllAsync(int page = 1, int pageSize = 20)
        {
            return await _context.Events
                .Include(e => e.Images)
                .AsNoTracking()
                .OrderByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Events?> GetByClubIdAsync(int clubId)
        {
            return await _context.Events
                .Include(e => e.Images)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.ClubId == clubId);
        }



        public async Task<Events?> UpdateAsync(int id, Events updated)
        {
            var existing = await _context.Events.FindAsync(id);
            if (existing == null)
                return null;

            existing.Name = updated.Name;
            existing.Description = updated.Description;
            existing.Location = updated.Location;
            existing.isPrivate = updated.isPrivate;
            existing.maxParticipants = updated.maxParticipants;
            existing.registerCost = updated.registerCost;
            existing.StartTime = updated.StartTime;
            existing.EndTime = updated.EndTime;
            existing.ClubId = updated.ClubId;
            existing.Category = updated.Category;
            existing.VenueName = updated.VenueName;
            existing.City = updated.City;
            existing.Latitude = updated.Latitude;
            existing.Longitude = updated.Longitude;
            existing.Tags = updated.Tags ?? new List<string>();
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _context.Entry(existing).Collection(e => e.Images).LoadAsync();
            return existing;
        }

        public async Task<bool> UpdatePartialAsync(int id, Action<Events> patch)
        {
            var events = await _context.Events.FindAsync(id);
            if (events == null)
                return false;

            patch(events);
            events.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var events = await _context.Events.FindAsync(id);
            if (events == null)
                return false;

            _context.Events.Remove(events);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            return await _context.Events.AnyAsync(e => e.Id == id);
        }

        public async Task<List<Events>> SearchAsync(EventSearchCriteria criteria)
        {
            var now = DateTime.UtcNow;

            IQueryable<Events> query = _context.Events
                .Include(e => e.Images)
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(criteria.Query))
            {
                var term = criteria.Query.Trim();
                query = query.Where(e =>
                    EF.Functions.Like(e.Name, $"%{term}%") ||
                    EF.Functions.Like(e.Description, $"%{term}%") ||
                    EF.Functions.Like(e.Location, $"%{term}%") ||
                    (e.VenueName != null && EF.Functions.Like(e.VenueName, $"%{term}%")) ||
                    (e.City != null && EF.Functions.Like(e.City, $"%{term}%"))
                );
            }

            query = query.Where(e => e.isPrivate == criteria.IsPrivate);

            if (criteria.Category.HasValue)
                query = query.Where(e => e.Category == criteria.Category.Value);

            if (!string.IsNullOrWhiteSpace(criteria.City))
            {
                var city = criteria.City.Trim();
                query = query.Where(e => e.City == city);
            }

            if (criteria.Status == EventStatus.Upcoming)
                query = query.Where(e => e.StartTime > now);
            else if (criteria.Status == EventStatus.Ongoing)
                query = query.Where(e => e.StartTime <= now && (e.EndTime == null || e.EndTime > now));
            else if (criteria.Status == EventStatus.Closed)
                query = query.Where(e => e.EndTime != null && e.EndTime <= now);

            query = criteria.SortBy switch
            {
                EventSortBy.Date => query.OrderBy(e => e.StartTime),
                EventSortBy.Popularity => query.OrderByDescending(e => e.RegistrationCount)
                    .ThenByDescending(e => e.CreatedAt),
                _ => query.OrderByDescending(e => e.CreatedAt)
            };

            return await query
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToListAsync();
        }

        public async Task IncrementRegistrationCountAsync(int eventId, int delta)
        {
            await _context.Events
                .Where(e => e.Id == eventId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.RegistrationCount,
                        e => Math.Max(0, e.RegistrationCount + delta))
                    .SetProperty(e => e.UpdatedAt, DateTime.UtcNow));
        }

        public async Task<List<Events>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var idList = ids.Distinct().ToList();

            if (idList.Count == 0)
                return new List<Events>();

            return await _context.Events
                .Include(e => e.Images)
                .AsNoTracking()
                .Where(e => idList.Contains(e.Id))
                .ToListAsync();
        }

        public async Task<List<Events>> GetAllForReindexAsync(int page, int pageSize) =>
            await _context.Events
                .AsNoTracking()
                .OrderBy(e => e.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

        public async Task<List<Events>> CreateManyAsync(IEnumerable<Events> events)
        {
            var list = events.ToList();
            await _context.Events.AddRangeAsync(list);
            await _context.SaveChangesAsync();
            return list;
        }

        public async Task<int> UpdateManyAsync(IEnumerable<(int id, Action<Events> patch)> updates)
        {
            int count = 0;
            foreach (var (id, patch) in updates)
            {
                var ev = await _context.Events.FindAsync(id);
                if (ev == null) continue;
                patch(ev);
                ev.UpdatedAt = DateTime.UtcNow;
                count++;
            }
            if (count > 0)
                await _context.SaveChangesAsync();
            return count;
        }

        public async Task<int> DeleteManyAsync(IEnumerable<int> ids)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return 0;
            return await _context.Events
                .Where(e => idList.Contains(e.Id))
                .ExecuteDeleteAsync();
        }
    }
}
