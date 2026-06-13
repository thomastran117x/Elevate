using System.Security.Claims;

using backend.main.features.clubs.reviews;
using backend.main.features.clubs.reviews.contracts.requests;
using backend.main.features.clubs.reviews.contracts.responses;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubReviewControllerTests
{
    [Fact]
    public async Task CreateReview_ShouldReturnCreatedResponse()
    {
        var service = new Mock<IClubReviewService>();
        service.Setup(s => s.CreateReviewAsync(4, 7, "Great club", 5, "Loved it"))
            .ReturnsAsync(new ClubReview
            {
                Id = 9,
                ClubId = 4,
                UserId = 7,
                Title = "Great club",
                Rating = 5,
                Comment = "Loved it"
            });

        var controller = CreateController(service.Object);

        var result = await controller.CreateReview(4, new ClubReviewCreateRequest
        {
            Title = "Great club",
            Rating = 5,
            Comment = "Loved it"
        });

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<ClubReviewResponse>>().Subject;
        response.Data!.Rating.Should().Be(5);
        response.Data.Title.Should().Be("Great club");
    }

    [Fact]
    public async Task DeleteReview_ShouldReturnMessageResponse()
    {
        var service = new Mock<IClubReviewService>();
        var controller = CreateController(service.Object);

        var result = await controller.DeleteReview(4, 9);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<MessageResponse>().Subject;
        response.Message.Should().Contain("Review with ID 9 has been deleted successfully.");
        service.Verify(s => s.DeleteReviewAsync(4, 9, 7), Times.Once);
    }

    private static ClubReviewController CreateController(IClubReviewService service)
    {
        var controller = new ClubReviewController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "reviewer@example.com"),
                    new Claim(ClaimTypes.Role, "Participant")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
