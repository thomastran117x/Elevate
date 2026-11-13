using backend.main.application.security.authorization;
using backend.main.features.cache;
using backend.main.features.clubs.search;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

namespace backend.main.features.clubs.reviews
{
    public class ClubReviewService : IClubReviewService
    {
        private readonly AppDatabaseContext _db;
        private readonly IClubReviewRepository _reviewRepository;
        private readonly IClubRepository _clubRepository;
        private readonly IUserRepository _userRepository;
        private readonly IClubSearchOutboxWriter _outboxWriter;
        private readonly ICacheService _cache;
        private readonly IResourceAuthorizer _authorizer;

        public ClubReviewService(
            AppDatabaseContext db,
            IClubReviewRepository reviewRepository,
            IClubRepository clubRepository,
            IUserRepository userRepository,
            IClubSearchOutboxWriter outboxWriter,
            ICacheService cache,
            IResourceAuthorizer authorizer)
        {
            _db = db;
            _reviewRepository = reviewRepository;
            _clubRepository = clubRepository;
            _userRepository = userRepository;
            _outboxWriter = outboxWriter;
            _cache = cache;
            _authorizer = authorizer;
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

        public async Task<(IReadOnlyList<ClubReview> Reviews, IReadOnlyDictionary<int, UserListRecord> Users, int TotalCount)>
            GetClubReviewsAsync(int clubId, int page, int pageSize)
        {
            var clubExists = await _clubRepository.ExistsAsync(clubId);
            if (!clubExists)
                throw new ResourceNotFoundException($"Club with ID {clubId} was not found.");

            var reviews = await _reviewRepository.GetByClubIdAsync(clubId, page, pageSize);
            var totalCount = await _reviewRepository.CountByClubIdAsync(clubId);

            var userIds = reviews.Select(r => r.UserId).Distinct().ToList();
            IReadOnlyDictionary<int, UserListRecord> users = userIds.Count == 0
                ? new Dictionary<int, UserListRecord>()
                : (await _userRepository.GetByIdsAsync(userIds)).ToDictionary(u => u.Id);

            return (reviews, users, totalCount);
        }

        public async Task<List<ClubReview>> GetReviewsByUserAsync(int userId, int page, int pageSize)
        {
            return await _reviewRepository.GetByUserIdAsync(userId, page, pageSize);
        }

        public async Task<ClubReview> UpdateReviewAsync(int clubId, int reviewId, int userId, string title, int rating, string? comment)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId)
                ?? throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            if (review.ClubId != clubId)
                throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            await EnsureAuthorAsync(review, userId, "update");

            var updated = await _reviewRepository.UpdateAsync(reviewId, new ClubReview
            {
                Title = title,
                Rating = rating,
                Comment = comment
            }) ?? throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            await UpdateClubRatingAsync(review.ClubId);

            return updated;
        }

        public async Task DeleteReviewAsync(int clubId, int reviewId, int userId)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId)
                ?? throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            if (review.ClubId != clubId)
                throw new ResourceNotFoundException($"Review with ID {reviewId} was not found.");

            await EnsureAuthorAsync(review, userId, "delete");

            await _reviewRepository.DeleteAsync(reviewId);

            await UpdateClubRatingAsync(review.ClubId);
        }

        // Known deviation recorded in the matrix: reviews are author-only, with no staff moderation.
        private Task EnsureAuthorAsync(ClubReview review, int userId, string action) =>
            _authorizer.EnsureAsync(
                ResourceActor.User(userId, null),
                Permissions.ContentAuthor,
                new AuthoredContentResource("Review", review.Id, review.UserId),
                new PermissionMessages($"You are not allowed to {action} this review."));

        private async Task UpdateClubRatingAsync(int clubId)
        {
            var average = await _reviewRepository.GetAverageRatingAsync(clubId);

            var updated = await _clubRepository.UpdatePartialAsync(clubId, club =>
            {
                club.Rating = average.HasValue ? Math.Round(average.Value, 1) : null;
            });

            if (!updated)
                return;

            var club = await _db.Clubs.FindAsync(clubId);
            if (club == null)
                return;

            _outboxWriter.StageUpsert(club);
            await _db.SaveChangesAsync();
            await _cache.DeleteKeyAsync($"club:{clubId}");
            await _cache.IncrementAsync("clubs:version");
        }
    }
}

