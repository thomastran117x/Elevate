using backend.main.features.clubs.reviews;

namespace backend.main.features.clubs.reviews
{
    public interface IClubReviewService
    {
        Task<ClubReview> CreateReviewAsync(int clubId, int userId, string title, int rating, string? comment);
        Task<List<ClubReview>> GetReviewsByClubAsync(int clubId, int page, int pageSize);
        Task<List<ClubReview>> GetReviewsByUserAsync(int userId, int page, int pageSize);
        Task<ClubReview> UpdateReviewAsync(int reviewId, int userId, string title, int rating, string? comment);
        Task DeleteReviewAsync(int reviewId, int userId);
    }
}


