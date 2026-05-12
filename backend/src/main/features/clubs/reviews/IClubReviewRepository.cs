using backend.main.models.core;

namespace backend.main.features.clubs.reviews
{
    public interface IClubReviewRepository
    {
        Task<ClubReview> CreateAsync(ClubReview review);
        Task<List<ClubReview>> GetByClubIdAsync(int clubId, int page, int pageSize);
        Task<List<ClubReview>> GetByUserIdAsync(int userId, int page, int pageSize);
        Task<ClubReview?> GetByIdAsync(int id);
        Task<ClubReview?> UpdateAsync(int id, ClubReview updated);
        Task<bool> DeleteAsync(int id);
        Task<double?> GetAverageRatingAsync(int clubId);
    }
}
