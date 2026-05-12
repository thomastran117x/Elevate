using backend.main.infrastructure.database.core;
using backend.main.features.clubs;

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

        public async Task<List<Club>> SearchAsync(
            string? search,
            int page = 1,
            int pageSize = 20)
        {
            IQueryable<Club> query = _context.Clubs
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();

                query = query.Where(c =>
                    EF.Functions.Like(c.Name, $"%{term}%") ||
                    EF.Functions.Like(c.Description, $"%{term}%")
                );
            }

            return await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
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
    }
}

