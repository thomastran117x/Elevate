using backend.main.application.security;
using backend.main.features.clubs.reviews.contracts.requests;
using backend.main.features.clubs.reviews.contracts.responses;
using backend.main.dtos.responses.general;
using backend.main.models.core;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.reviews
{
    [ApiController]
    [Route("clubs")]
    public class ClubReviewController : ControllerBase
    {
        private readonly IClubReviewService _reviewService;

        public ClubReviewController(IClubReviewService reviewService)
        {
            _reviewService = reviewService;
        }

        [Authorize]
        [HttpPost("{clubId}/reviews")]
        public async Task<IActionResult> CreateReview(int clubId, [FromBody] ClubReviewCreateRequest request)
        {
            var userPayload = User.GetUserPayload();

            ClubReview review = await _reviewService.CreateReviewAsync(
                clubId: clubId,
                userId: userPayload.Id,
                title: request.Title,
                rating: request.Rating,
                comment: request.Comment
            );

            ClubReviewResponse response = MapToResponse(review);

            return StatusCode(
                201,
                new ApiResponse<ClubReviewResponse>(
                    $"Review for club with ID {clubId} has been submitted successfully.",
                    response
                )
            );
        }

        [HttpGet("{clubId}/reviews")]
        public async Task<IActionResult> GetReviewsByClub(
            int clubId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            List<ClubReview> reviews = await _reviewService.GetReviewsByClubAsync(clubId, page, pageSize);

            IEnumerable<ClubReviewResponse> responses = reviews.Select(MapToResponse);

            return StatusCode(
                200,
                new ApiResponse<IEnumerable<ClubReviewResponse>>(
                    $"Reviews for club with ID {clubId} have been fetched successfully.",
                    responses
                )
            );
        }

        [Authorize]
        [HttpPut("{clubId}/reviews/{reviewId}")]
        public async Task<IActionResult> UpdateReview(int clubId, int reviewId, [FromBody] ClubReviewUpdateRequest request)
        {
            var userPayload = User.GetUserPayload();

            ClubReview review = await _reviewService.UpdateReviewAsync(
                reviewId: reviewId,
                userId: userPayload.Id,
                title: request.Title,
                rating: request.Rating,
                comment: request.Comment
            );

            ClubReviewResponse response = MapToResponse(review);

            return StatusCode(
                200,
                new ApiResponse<ClubReviewResponse>(
                    $"Review with ID {reviewId} has been updated successfully.",
                    response
                )
            );
        }

        [Authorize]
        [HttpDelete("{clubId}/reviews/{reviewId}")]
        public async Task<IActionResult> DeleteReview(int clubId, int reviewId)
        {
            var userPayload = User.GetUserPayload();

            await _reviewService.DeleteReviewAsync(reviewId, userPayload.Id);

            return StatusCode(
                200,
                new MessageResponse(
                    $"Review with ID {reviewId} has been deleted successfully."
                )
            );
        }

        private static ClubReviewResponse MapToResponse(ClubReview review)
        {
            return new ClubReviewResponse(
                review.Id,
                review.UserId,
                review.ClubId,
                review.Title,
                review.Rating,
                review.Comment,
                review.CreatedAt
            );
        }
    }

    [ApiController]
    [Route("users")]
    public class UserReviewController : ControllerBase
    {
        private readonly IClubReviewService _reviewService;

        public UserReviewController(IClubReviewService reviewService)
        {
            _reviewService = reviewService;
        }

        [Authorize("AdminOnly")]
        [HttpGet("{userId}/reviews")]
        public async Task<IActionResult> GetReviewsByUser(
            int userId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            List<ClubReview> reviews = await _reviewService.GetReviewsByUserAsync(userId, page, pageSize);

            IEnumerable<ClubReviewResponse> responses = reviews.Select(r => new ClubReviewResponse(
                r.Id,
                r.UserId,
                r.ClubId,
                r.Title,
                r.Rating,
                r.Comment,
                r.CreatedAt
            ));

            return StatusCode(
                200,
                new ApiResponse<IEnumerable<ClubReviewResponse>>(
                    $"Reviews submitted by user with ID {userId} have been fetched successfully.",
                    responses
                )
            );
        }
    }
}
