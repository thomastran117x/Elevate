using backend.main.infrastructure.database.core;
using backend.main.models.core;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class PostCommentRepository : IPostCommentRepository
    {
        private readonly AppDatabaseContext _context;

        public PostCommentRepository(AppDatabaseContext context) => _context = context;

        public async Task<PostComment> CreateAsync(PostComment comment)
        {
            _context.PostComments.Add(comment);
            await _context.SaveChangesAsync();
            return comment;
        }

        public async Task<PostComment?> GetByIdAsync(int id)
        {
            return await _context.PostComments
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<List<PostComment>> GetByPostIdAsync(int postId, int page, int pageSize)
        {
            return await _context.PostComments
                .AsNoTracking()
                .Where(c => c.PostId == postId)
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> CountByPostIdAsync(int postId)
        {
            return await _context.PostComments
                .CountAsync(c => c.PostId == postId);
        }

        public async Task<PostComment?> UpdateAsync(int id, PostComment updated)
        {
            var existing = await _context.PostComments.FindAsync(id);
            if (existing == null)
                return null;

            existing.Content = updated.Content;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var comment = await _context.PostComments.FindAsync(id);
            if (comment == null)
                return false;

            _context.PostComments.Remove(comment);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
