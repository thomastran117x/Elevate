using System.Security.Claims;

using backend.main.features.clubs.discussions;
using backend.main.features.clubs.discussions.contracts.requests;
using backend.main.features.clubs.discussions.contracts.responses;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubDiscussionControllerTests
{
    [Fact]
    public async Task CreateDiscussion_ShouldReturnCreatedResponse()
    {
        var service = new Mock<IClubDiscussionService>();
        service.Setup(s => s.CreateAsync(4, 7, "Participant", "Weekend ride", "Where should we go?"))
            .ReturnsAsync(new ClubDiscussion
            {
                Id = 9,
                ClubId = 4,
                UserId = 7,
                Title = "Weekend ride",
                Description = "Where should we go?"
            });

        var controller = CreateController(service.Object);

        var result = await controller.CreateDiscussion(4, new ClubDiscussionCreateRequest
        {
            Title = "Weekend ride",
            Description = "Where should we go?"
        });

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<ClubDiscussionResponse>>().Subject;
        response.Data!.Title.Should().Be("Weekend ride");
        response.Data.Description.Should().Be("Where should we go?");
        response.Data.Author!.Id.Should().Be(7);
    }

    [Fact]
    public async Task GetDiscussionsByClub_ShouldReturnPagedResponseWithAuthors()
    {
        var service = new Mock<IClubDiscussionService>();
        service.Setup(s => s.GetByClubIdAsync(4, 7, "Participant", 1, 20))
            .ReturnsAsync((
                new List<ClubDiscussion>
                {
                    new() { Id = 2, ClubId = 4, UserId = 7, Title = "Newer", Description = "B" },
                    new() { Id = 1, ClubId = 4, UserId = 8, Title = "Older", Description = "A" }
                },
                new Dictionary<int, UserListRecord>
                {
                    [7] = new() { Id = 7, Email = "a@test.local", Username = "rider", Name = "Rider", Usertype = "Participant" }
                },
                2));

        var controller = CreateController(service.Object);

        var result = await controller.GetDiscussionsByClub(4);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<ClubDiscussionResponse>>>().Subject;

        var items = response.Data!.Items.ToList();
        items.Should().HaveCount(2);
        items[0].Title.Should().Be("Newer");
        items[0].Author!.Name.Should().Be("Rider");
        // No author record for user 8 — the response still carries the id, with null display fields.
        items[1].Author!.Id.Should().Be(8);
        items[1].Author!.Name.Should().BeNull();
        response.Data.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDiscussionsByClub_ShouldOmitTheUser_WhenTheRequestIsAnonymous()
    {
        var service = new Mock<IClubDiscussionService>();
        service.Setup(s => s.GetByClubIdAsync(4, null, null, 1, 20))
            .ReturnsAsync(([], new Dictionary<int, UserListRecord>(), 0));

        var controller = CreateController(service.Object, authenticated: false);

        var result = await controller.GetDiscussionsByClub(4);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(200);
        service.Verify(s => s.GetByClubIdAsync(4, null, null, 1, 20), Times.Once);
    }

    [Theory]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-5, 500, 1, 100)]
    [InlineData(3, 50, 3, 50)]
    public async Task GetDiscussionsByClub_ShouldClampPaging(
        int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var service = new Mock<IClubDiscussionService>();
        service.Setup(s => s.GetByClubIdAsync(4, 7, "Participant", expectedPage, expectedPageSize))
            .ReturnsAsync(([], new Dictionary<int, UserListRecord>(), 0));

        var controller = CreateController(service.Object);

        await controller.GetDiscussionsByClub(4, page, pageSize);

        service.Verify(s => s.GetByClubIdAsync(4, 7, "Participant", expectedPage, expectedPageSize), Times.Once);
    }

    [Fact]
    public async Task UpdateDiscussion_ShouldReturnUpdatedResponse()
    {
        var service = new Mock<IClubDiscussionService>();
        service.Setup(s => s.UpdateAsync(4, 9, 7, "Updated", "Updated body"))
            .ReturnsAsync(new ClubDiscussion
            {
                Id = 9,
                ClubId = 4,
                UserId = 7,
                Title = "Updated",
                Description = "Updated body"
            });

        var controller = CreateController(service.Object);

        var result = await controller.UpdateDiscussion(4, 9, new ClubDiscussionUpdateRequest
        {
            Title = "Updated",
            Description = "Updated body"
        });

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<ClubDiscussionResponse>>().Subject;
        response.Data!.Title.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteDiscussion_ShouldReturnMessageResponse()
    {
        var service = new Mock<IClubDiscussionService>();
        var controller = CreateController(service.Object);

        var result = await controller.DeleteDiscussion(4, 9);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<MessageResponse>().Subject;
        response.Message.Should().Contain("Discussion with ID 9 has been deleted successfully.");
        service.Verify(s => s.DeleteAsync(4, 9, 7), Times.Once);
    }

    private static ClubDiscussionController CreateController(
        IClubDiscussionService service, bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "member@example.com"),
                    new Claim(ClaimTypes.Role, "Participant")
                ], "TestAuth")
            : new ClaimsIdentity();

        return new ClubDiscussionController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }
}
