using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class EventsRepository : BaseRepository, IEventsRepository
    {
        public EventsRepository(AppDatabaseContext context) : base(context) { }

        public async Task<Events> CreateAsync(Events events)
        {
            return await ExecuteAsync(async () =>
            {
                _context.Events.Add(events);
                await _context.SaveChangesAsync();
                return events;
            })!;
        }

        public async Task<Events?> GetByIdAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Events
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == id);
            });
        }

        public async Task<IEnumerable<Events>> GetAllAsync(int page = 1, int pageSize = 20)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Events
                    .AsNoTracking()
                    .OrderByDescending(e => e.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            })!;
        }

        public async Task<Events?> GetByClubIdAsync(int clubId)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Events
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.ClubId == clubId);
            });
        }

        public async Task<Events?> UpdateAsync(int id, Events updated)
        {
            return await ExecuteAsync(async () =>
            {
                var existing = await _context.Events.FindAsync(id);
                if (existing == null)
                    return null;

                existing.Name = updated.Name;
                existing.Description = updated.Description;
                existing.Location = updated.Location;
                existing.ImageUrl = updated.ImageUrl;
                existing.isPrivate = updated.isPrivate;
                existing.maxParticipants = updated.maxParticipants;
                existing.registerCost = updated.registerCost;
                existing.StartTime = updated.StartTime;
                existing.EndTime = updated.EndTime;
                existing.ClubId = updated.ClubId;
                existing.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return existing;
            });
        }

        public async Task<bool> UpdatePartialAsync(int id, Action<Events> patch)
        {
            return await ExecuteAsync(async () =>
            {
                var events = await _context.Events.FindAsync(id);
                if (events == null)
                    return false;

                patch(events);
                events.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            })!;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                var events = await _context.Events.FindAsync(id);
                if (events == null)
                    return false;

                _context.Events.Remove(events);
                await _context.SaveChangesAsync();
                return true;
            })!;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Events.AnyAsync(e => e.Id == id);
            })!;
        }

        public async Task<List<Events>> SearchAsync(
            string? search,
            bool isPrivate,
            bool isAvaliable,
            int page = 1,
            int pageSize = 20)
        {
            return await ExecuteAsync(async () =>
            {
                IQueryable<Events> query = _context.Events
                    .AsNoTracking();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim();

                    query = query.Where(e =>
                        EF.Functions.Like(e.Name, $"%{term}%") ||
                        EF.Functions.Like(e.Description, $"%{term}%") ||
                        EF.Functions.Like(e.Location, $"%{term}%")
                    );
                }

                query = query.Where(e => e.isPrivate == isPrivate);

                if (isAvaliable)
                {
                    query = query.Where(e =>
                        e.EndTime == null || e.EndTime > DateTime.UtcNow);
                }

                return await query
                    .OrderByDescending(e => e.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            })!;
        }

        public async Task<List<Events>> GetByIdsAsync(IEnumerable<int> ids)
        {
            return await ExecuteAsync(async () =>
            {
                var idList = ids.Distinct().ToList();

                if (idList.Count == 0)
                    return new List<Events>();

                return await _context.Events
                    .AsNoTracking()
                    .Where(e => idList.Contains(e.Id))
                    .ToListAsync();
            })!;
        }
    }
}
