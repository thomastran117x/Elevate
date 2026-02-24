
using backend.main.Interfaces;
using backend.main.Models;
using backend.main.Resources;

using Microsoft.EntityFrameworkCore;

namespace backend.main.Repositories
{
    public class FollowRepository : BaseRepository, IFollowRepository
    {
        public FollowRepository(AppDatabaseContext context) : base(context) { }

        public async Task<FollowClub> FollowClubAsync(int clubId, int userId)
        {
            return await ExecuteAsync(async () =>
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
            });
        }

        public async Task<FollowClub?> GetFollowAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.FollowClubs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == id);
            });
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.FollowClubs
                    .AsNoTracking()
                    .OrderByDescending(f => f.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            })!;
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await ExecuteAsync(() =>
                _context.FollowClubs
                    .AsNoTracking()
                    .Where(f => f.ClubId == clubId)
                    .OrderByDescending(f => f.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
            );
        }

        public async Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            return await ExecuteAsync(() =>
                _context.FollowClubs
                    .AsNoTracking()
                    .Where(f => f.UserId == userId)
                    .OrderByDescending(f => f.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
            );
        }

        public async Task<FollowClub?> IsFollowingClubAsync(int clubId, int userId)
        {
            return await ExecuteAsync(() =>
                _context.FollowClubs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f =>
                        f.ClubId == clubId &&
                        f.UserId == userId
                    )
            );
        }

        public async Task<bool> UnfollowClubAsync(int clubId, int userId)
        {
            return await ExecuteAsync(async () =>
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
            });
        }

        public async Task<bool> UnfollowClubAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                var follow = await _context.FollowClubs
                    .FirstOrDefaultAsync(f => f.Id == id);

                if (follow == null)
                    return false;

                _context.FollowClubs.Remove(follow);
                await _context.SaveChangesAsync();

                return true;
            });
        }
    }
}
