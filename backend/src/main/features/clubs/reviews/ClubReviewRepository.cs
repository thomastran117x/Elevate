using backend.main.infrastructure.database.core;
using backend.main.models.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.clubs.reviews
{
    public class ClubReviewRepository : IClubReviewRepository
    {
        private readonly AppDatabaseContext _context;

        public ClubReviewRepository(AppDatabaseContext context) => _context = context;

        public async Task<ClubReview> CreateAsync(ClubReview review)
        {
            _context.ClubReviews.Add(review);
            await _context.SaveChangesAsync();
            return review;
        }

        public async Task<List<ClubReview>> GetByClubIdAsync(int clubId, int page, int pageSize)
        {
            return await _context.ClubReviews
                .AsNoTracking()
                .Where(r => r.ClubId == clubId)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<List<ClubReview>> GetByUserIdAsync(int userId, int page, int pageSize)
        {
            return await _context.ClubReviews
                .AsNoTracking()
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<ClubReview?> GetByIdAsync(int id)
        {
            return await _context.ClubReviews
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<ClubReview?> UpdateAsync(int id, ClubReview updated)
        {
            var existing = await _context.ClubReviews.FindAsync(id);
            if (existing == null)
                return null;

            existing.Title = updated.Title;
            existing.Rating = updated.Rating;
            existing.Comment = updated.Comment;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var review = await _context.ClubReviews.FindAsync(id);
            if (review == null)
                return false;

            _context.ClubReviews.Remove(review);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<double?> GetAverageRatingAsync(int clubId)
        {
            var hasReviews = await _context.ClubReviews.AnyAsync(r => r.ClubId == clubId);
            if (!hasReviews)
                return null;

            return await _context.ClubReviews
                .Where(r => r.ClubId == clubId)
                .AverageAsync(r => (double)r.Rating);
        }
    }
}
