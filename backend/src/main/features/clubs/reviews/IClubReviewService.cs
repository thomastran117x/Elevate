using backend.main.features.clubs.reviews;
using backend.main.features.profile.contracts;

namespace backend.main.features.clubs.reviews
{
    public interface IClubReviewService
    {
        Task<ClubReview> CreateReviewAsync(int clubId, int userId, string title, int rating, string? comment);
        Task<List<ClubReview>> GetReviewsByClubAsync(int clubId, int page, int pageSize);
        /// <summary>
        /// Returns a page of club reviews enriched with each reviewer's public profile
        /// fields, plus the total review count for the club.
        /// </summary>
        Task<(IReadOnlyList<ClubReview> Reviews, IReadOnlyDictionary<int, UserListRecord> Users, int TotalCount)>
            GetClubReviewsAsync(int clubId, int page, int pageSize);
        Task<List<ClubReview>> GetReviewsByUserAsync(int userId, int page, int pageSize);
        Task<ClubReview> UpdateReviewAsync(int clubId, int reviewId, int userId, string title, int rating, string? comment);
        Task DeleteReviewAsync(int clubId, int reviewId, int userId);
    }
}

