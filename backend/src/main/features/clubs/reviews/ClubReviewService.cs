using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.reviews
{
    public class ClubReviewService : IClubReviewService
    {
        private readonly IClubReviewRepository _reviewRepository;
        private readonly IClubRepository _clubRepository;

        public ClubReviewService(
            IClubReviewRepository reviewRepository,
            IClubRepository clubRepository)
        {
            _reviewRepository = reviewRepository;
            _clubRepository = clubRepository;
        }

        public async Task<ClubReview> CreateReviewAsync(int clubId, int userId, string title, int rating, string? comment)
        {
            var clubExists = await _clubRepository.ExistsAsync(clubId);
            if (!clubExists)
                throw new ResourceNotFoundException($"Club with ID {clubId} was not found.");

            var review = new ClubReview
            {
                ClubId = clubId,
                UserId = userId,
                Title = title,
                Rating = rating,
                Comment = comment
            };

            var created = await _reviewRepository.CreateAsync(review);

            await UpdateClubRatingAsync(clubId);

            return created;
        }

        public async Task<List<ClubReview>> GetReviewsByClubAsync(int clubId, int page, int pageSize)
        {
            var clubExists = await _clubRepository.ExistsAsync(clubId);
            if (!clubExists)
                throw new ResourceNotFoundException($"Club with ID {clubId} was not found.");

            return await _reviewRepository.GetByClubIdAsync(clubId, page, pageSize);
        }

        public async Task<List<ClubReview>> GetReviewsByUserAsync(int userId, int page, int pageSize)
        {
            return await _reviewRepository.GetByUserIdAsync(userId, page, pageSize);
        }

        public async Task<ClubReview> UpdateReviewAsync(int reviewId, int userId, string title, int rating, string? comment)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId)
                ?? throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            if (review.UserId != userId)
                throw new ForbiddenException("You are not allowed to update this review.");

            var updated = await _reviewRepository.UpdateAsync(reviewId, new ClubReview
            {
                Title = title,
                Rating = rating,
                Comment = comment
            }) ?? throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            await UpdateClubRatingAsync(review.ClubId);

            return updated;
        }

        public async Task DeleteReviewAsync(int reviewId, int userId)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId)
                ?? throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            if (review.UserId != userId)
                throw new ForbiddenException("You are not allowed to delete this review.");

            await _reviewRepository.DeleteAsync(reviewId);

            await UpdateClubRatingAsync(review.ClubId);
        }

        private async Task UpdateClubRatingAsync(int clubId)
        {
            var average = await _reviewRepository.GetAverageRatingAsync(clubId);

            await _clubRepository.UpdatePartialAsync(clubId, club =>
            {
                club.Rating = average.HasValue ? Math.Round(average.Value, 1) : null;
            });
        }
    }
}


