using backend.main.features.clubs.follow;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.follow
{
    public class FollowRepository : IFollowRepository
    {
        private readonly AppDatabaseContext _context;

        public FollowRepository(AppDatabaseContext context) => _context = context;

        public async Task<FollowClub> FollowClubAsync(int clubId, int userId)
        {
            var follow = new FollowClub
            {
                ClubId = clubId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.FollowClubs.Add(follow);
            await _context.SaveChangesAsync();

            return follow;
        }

        public async Task<FollowClub?> GetFollowAsync(int id)
        {
            return await _context.FollowClubs
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20)
        {
            return await _context.FollowClubs
                .AsNoTracking()
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await _context.FollowClubs
                .AsNoTracking()
                .Where(f => f.ClubId == clubId)
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountFollowsByClubAsync(int clubId)
        {
            return await _context.FollowClubs
                .AsNoTracking()
                .CountAsync(f => f.ClubId == clubId);
        }

        public async Task<(IReadOnlyList<FollowClub> Members, int TotalCount)> SearchClubMembersAsync(
            int clubId, string? search, int page, int pageSize)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.FollowClubs
                .AsNoTracking()
                .Where(f => f.ClubId == clubId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                query =
                    from f in query
                    join u in _context.Users.AsNoTracking() on f.UserId equals u.Id
                    where (u.Name != null && EF.Functions.Like(u.Name, term)) ||
                          (u.Username != null && EF.Functions.Like(u.Username, term))
                    select f;
            }

            var totalCount = await query.CountAsync();
            var members = await query
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (members, totalCount);
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await _context.FollowClubs
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<FollowClub?> IsFollowingClubAsync(int clubId, int userId)
        {
            return await _context.FollowClubs
                .AsNoTracking()
                .FirstOrDefaultAsync(f =>
                    f.ClubId == clubId &&
                    f.UserId == userId
                );
        }

        public async Task<bool> UnfollowClubAsync(int clubId, int userId)
        {
            var follow = await _context.FollowClubs
                .FirstOrDefaultAsync(f =>
                    f.ClubId == clubId &&
                    f.UserId == userId
                );

            if (follow == null)
                return false;

            _context.FollowClubs.Remove(follow);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> UnfollowClubAsync(int id)
        {
            var follow = await _context.FollowClubs
                .FirstOrDefaultAsync(f => f.Id == id);

            if (follow == null)
                return false;

            _context.FollowClubs.Remove(follow);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}


