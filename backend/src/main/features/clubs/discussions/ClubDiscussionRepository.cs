using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.discussions
{
    public class ClubDiscussionRepository : IClubDiscussionRepository
    {
        private readonly AppDatabaseContext _context;

        public ClubDiscussionRepository(AppDatabaseContext context) => _context = context;

        public async Task<ClubDiscussion> CreateAsync(ClubDiscussion discussion)
        {
            _context.ClubDiscussions.Add(discussion);
            await _context.SaveChangesAsync();
            return discussion;
        }

        public async Task<List<ClubDiscussion>> GetByClubIdAsync(int clubId, int page, int pageSize)
        {
            // Newest first. The Id tiebreaker keeps paging stable when two rows share a timestamp.
            return await _context.ClubDiscussions
                .AsNoTracking()
                .Where(d => d.ClubId == clubId)
                .OrderByDescending(d => d.CreatedAt)
                .ThenByDescending(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new ClubDiscussion
                {
                    Id = d.Id,
                    ClubId = d.ClubId,
                    UserId = d.UserId,
                    Title = d.Title,
                    Description = d.Description,
                    CreatedAt = d.CreatedAt,
                    UpdatedAt = d.UpdatedAt,
                    ReplyCount = _context.ClubDiscussionReplies.Count(r => r.DiscussionId == d.Id)
                })
                .ToListAsync();
        }

        public async Task<int> CountByClubIdAsync(int clubId)
        {
            return await _context.ClubDiscussions
                .AsNoTracking()
                .CountAsync(d => d.ClubId == clubId);
        }

        public async Task<ClubDiscussion?> GetByIdAsync(int id)
        {
            return await _context.ClubDiscussions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task<ClubDiscussion?> UpdateAsync(int id, ClubDiscussion updated)
        {
            var existing = await _context.ClubDiscussions.FindAsync(id);
            if (existing == null)
                return null;

            existing.Title = updated.Title;
            existing.Description = updated.Description;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var discussion = await _context.ClubDiscussions.FindAsync(id);
            if (discussion == null)
                return false;

            _context.ClubDiscussions.Remove(discussion);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
