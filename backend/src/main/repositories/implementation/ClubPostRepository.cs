using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class ClubPostRepository : IClubPostRepository
    {
        private readonly AppDatabaseContext _context;

        public ClubPostRepository(AppDatabaseContext context) => _context = context;

        public async Task<ClubPost> CreateAsync(ClubPost post)
        {
            _context.ClubPosts.Add(post);
            await _context.SaveChangesAsync();
            return post;
        }

        public async Task<ClubPost?> GetByIdAsync(int id)
        {
            return await _context.ClubPosts
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<List<ClubPost>> GetByClubIdAsync(int clubId, string? search, int page, int pageSize)
        {
            IQueryable<ClubPost> query = _context.ClubPosts
                .AsNoTracking()
                .Where(p => p.ClubId == clubId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(p =>
                    EF.Functions.Like(p.Title, $"%{term}%") ||
                    EF.Functions.Like(p.Content, $"%{term}%"));
            }

            return await query
                .OrderByDescending(p => p.IsPinned)
                .ThenByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountByClubIdAsync(int clubId, string? search)
        {
            IQueryable<ClubPost> query = _context.ClubPosts
                .Where(p => p.ClubId == clubId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(p =>
                    EF.Functions.Like(p.Title, $"%{term}%") ||
                    EF.Functions.Like(p.Content, $"%{term}%"));
            }

            return await query.CountAsync();
        }

        public async Task<List<ClubPost>> GetAllAsync(string? search, int page, int pageSize)
        {
            IQueryable<ClubPost> query = _context.ClubPosts.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(p =>
                    EF.Functions.Like(p.Title, $"%{term}%") ||
                    EF.Functions.Like(p.Content, $"%{term}%"));
            }

            return await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountAllAsync(string? search)
        {
            IQueryable<ClubPost> query = _context.ClubPosts.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string term = search.Trim();
                query = query.Where(p =>
                    EF.Functions.Like(p.Title, $"%{term}%") ||
                    EF.Functions.Like(p.Content, $"%{term}%"));
            }

            return await query.CountAsync();
        }

        public async Task<ClubPost?> UpdateAsync(int id, ClubPost updated)
        {
            var existing = await _context.ClubPosts.FindAsync(id);
            if (existing == null)
                return null;

            existing.Title = updated.Title;
            existing.Content = updated.Content;
            existing.PostType = updated.PostType;
            existing.IsPinned = updated.IsPinned;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var post = await _context.ClubPosts.FindAsync(id);
            if (post == null)
                return false;

            _context.ClubPosts.Remove(post);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task IncrementViewCountAsync(IEnumerable<int> postIds)
        {
            var ids = postIds.ToList();
            if (ids.Count == 0) return;

            await _context.ClubPosts
                .Where(p => ids.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1));
        }
    }
}
