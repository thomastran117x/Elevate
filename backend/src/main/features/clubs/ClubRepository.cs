using backend.main.infrastructure.database.core;
using backend.main.features.clubs.search;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs
{
    public class ClubRepository : IClubRepository
    {
        private readonly AppDatabaseContext _context;

        public ClubRepository(AppDatabaseContext context) => _context = context;

        public async Task<Club> CreateAsync(Club club)
        {
            _context.Clubs.Add(club);
            await _context.SaveChangesAsync();
            return club;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var club = await _context.Clubs.FindAsync(id);
            if (club == null)
                return false;

            _context.Clubs.Remove(club);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            return await _context.Clubs.AnyAsync(c => c.Id == id);
        }

        public async Task<IEnumerable<Club>> GetAllAsync(int page = 1, int pageSize = 20)
        {
            return await _context.Clubs
                .AsNoTracking()
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Club?> GetByIdAsync(int id)
        {
            return await _context.Clubs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<Club?> GetByUserIdAsync(int userId)
        {
            return await _context.Clubs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId);
        }

        public async Task<Club?> UpdateAsync(int id, Club updatedClub)
        {
            var existing = await _context.Clubs.FindAsync(id);
            if (existing == null)
                return null;

            existing.Name = updatedClub.Name;
            existing.Description = updatedClub.Description;
            existing.Clubtype = updatedClub.Clubtype;
            existing.ClubImage = updatedClub.ClubImage;
            existing.Phone = updatedClub.Phone;
            existing.Email = updatedClub.Email;
            existing.WebsiteUrl = updatedClub.WebsiteUrl;
            existing.Location = updatedClub.Location;
            existing.Rating = updatedClub.Rating;
            existing.MemberCount = updatedClub.MemberCount;
            existing.CurrentVersionNumber = updatedClub.CurrentVersionNumber;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> UpdatePartialAsync(int id, Action<Club> patch)
        {
            var club = await _context.Clubs.FindAsync(id);
            if (club == null)
                return false;

            patch(club);
            club.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(List<Club> Items, int TotalCount)> SearchAsync(ClubSearchCriteria criteria)
        {
            IQueryable<Club> query = _context.Clubs
                .AsNoTracking()
                .Where(c => !c.isPrivate);

            if (!string.IsNullOrWhiteSpace(criteria.Query))
            {
                var term = criteria.Query.Trim();

                query = query.Where(c =>
                    EF.Functions.Like(c.Name, $"%{term}%") ||
                    EF.Functions.Like(c.Description, $"%{term}%") ||
                    (c.Location != null && EF.Functions.Like(c.Location, $"%{term}%"))
                );
            }

            if (criteria.ClubType.HasValue)
                query = query.Where(c => c.Clubtype == criteria.ClubType.Value);

            var totalCount = await query.CountAsync();

            var ordered = criteria.SortBy switch
            {
                ClubSortBy.Newest => query
                    .OrderByDescending(c => c.CreatedAt)
                    .ThenBy(c => c.Id),
                ClubSortBy.Members => query
                    .OrderByDescending(c => c.MemberCount)
                    .ThenByDescending(c => c.CreatedAt)
                    .ThenBy(c => c.Id),
                ClubSortBy.Rating => query
                    .OrderByDescending(c => c.Rating.HasValue)
                    .ThenByDescending(c => c.Rating)
                    .ThenByDescending(c => c.MemberCount)
                    .ThenByDescending(c => c.CreatedAt)
                    .ThenBy(c => c.Id),
                _ => query
                    .OrderByDescending(c => c.CreatedAt)
                    .ThenBy(c => c.Id)
            };

            var items = await ordered
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<List<Club>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var idList = ids.Distinct().ToList();

            if (idList.Count == 0)
                return new List<Club>();

            return await _context.Clubs
                .AsNoTracking()
                .Where(c => idList.Contains(c.Id))
                .ToListAsync();
        }

        public async Task<List<Club>> GetAllForReindexAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
            await _context.Clubs
                .AsNoTracking()
                .OrderBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
    }
}

