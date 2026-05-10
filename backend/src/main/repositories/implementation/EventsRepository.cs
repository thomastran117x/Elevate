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

            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var events = await _context.Events.FindAsync(id);
            if (events == null)
                return false;

            _context.Events.Remove(events);
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            return await _context.Events.AnyAsync(e => e.Id == id);
        }

        public async Task<(List<Events> Items, int TotalCount)> SearchAsync(EventSearchCriteria criteria)
        {
            var now = DateTime.UtcNow;

            IQueryable<Events> query = _context.Events
                .Include(e => e.Images)
                .AsNoTracking();

            query = ApplyFilters(query, criteria, now);

            var totalCount = await query.CountAsync();

            var items = await ApplyOrdering(query, criteria)
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToListAsync();

            return (items, totalCount);
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

        public async Task<List<Events>> GetAllForReindexAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
            await _context.Events
                .AsNoTracking()
                .OrderBy(e => e.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        public async Task<List<Events>> CreateManyAsync(IEnumerable<Events> events)
        {
            var list = events.ToList();
            await _context.Events.AddRangeAsync(list);
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
            return count;
        }

        public async Task<int> DeleteManyAsync(IEnumerable<int> ids)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return 0;

            var entities = await _context.Events
                .Where(e => idList.Contains(e.Id))
                .ToListAsync();

            _context.Events.RemoveRange(entities);
            return entities.Count;
        }

        private static IQueryable<Events> ApplyFilters(
            IQueryable<Events> query,
            EventSearchCriteria criteria,
            DateTime now)
        {
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

            if (criteria.ClubId.HasValue)
                query = query.Where(e => e.ClubId == criteria.ClubId.Value);

            query = query.Where(e => e.isPrivate == criteria.IsPrivate);

            if (criteria.Category.HasValue)
                query = query.Where(e => e.Category == criteria.Category.Value);

            if (!string.IsNullOrWhiteSpace(criteria.City))
            {
                var city = criteria.City.Trim();
                query = query.Where(e => e.City == city);
            }

            if (criteria.Lat.HasValue && criteria.Lng.HasValue && criteria.RadiusKm.HasValue)
            {
                var lat = criteria.Lat.Value;
                var lng = criteria.Lng.Value;
                var radiusKm = criteria.RadiusKm.Value;
                var latScaleKm = 111.32;
                var lngScaleKm = Math.Cos(lat * Math.PI / 180.0) * 111.32;
                var radiusSquaredKm = radiusKm * radiusKm;

                query = query.Where(e =>
                    e.Latitude.HasValue &&
                    e.Longitude.HasValue &&
                    ((((e.Latitude.Value - lat) * latScaleKm) * ((e.Latitude.Value - lat) * latScaleKm)) +
                     (((e.Longitude.Value - lng) * lngScaleKm) * ((e.Longitude.Value - lng) * lngScaleKm)))
                    <= radiusSquaredKm
                );
            }

            if (criteria.Status == EventStatus.Upcoming)
                query = query.Where(e => e.StartTime > now);
            else if (criteria.Status == EventStatus.Ongoing)
                query = query.Where(e => e.StartTime <= now && (e.EndTime == null || e.EndTime > now));
            else if (criteria.Status == EventStatus.Closed)
                query = query.Where(e => e.EndTime != null && e.EndTime <= now);

            return query;
        }

        private static IOrderedQueryable<Events> ApplyOrdering(
            IQueryable<Events> query,
            EventSearchCriteria criteria)
        {
            if (criteria.SortBy == EventSortBy.Distance && criteria.Lat.HasValue && criteria.Lng.HasValue)
            {
                var lat = criteria.Lat.Value;
                var lng = criteria.Lng.Value;
                var latScaleKm = 111.32;
                var lngScaleKm = Math.Cos(lat * Math.PI / 180.0) * 111.32;

                return query
                    .OrderBy(e =>
                        (e.Latitude.HasValue && e.Longitude.HasValue)
                            ? ((((e.Latitude.Value - lat) * latScaleKm) * ((e.Latitude.Value - lat) * latScaleKm)) +
                               (((e.Longitude.Value - lng) * lngScaleKm) * ((e.Longitude.Value - lng) * lngScaleKm)))
                            : double.MaxValue)
                    .ThenBy(e => e.StartTime)
                    .ThenBy(e => e.Id);
            }

            return criteria.SortBy switch
            {
                EventSortBy.Date => query
                    .OrderBy(e => e.StartTime)
                    .ThenByDescending(e => e.CreatedAt)
                    .ThenBy(e => e.Id),
                EventSortBy.Popularity => query
                    .OrderByDescending(e => e.RegistrationCount)
                    .ThenBy(e => e.StartTime)
                    .ThenByDescending(e => e.CreatedAt)
                    .ThenBy(e => e.Id),
                _ => query
                    .OrderByDescending(e => e.CreatedAt)
                    .ThenBy(e => e.Id)
            };
        }
    }
}
