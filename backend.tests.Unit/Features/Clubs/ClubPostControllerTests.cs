using System.Security.Claims;

using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.contracts.requests;
using backend.main.features.clubs.posts.contracts.responses;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubPostControllerTests
{
    [Fact]
    public async Task CreatePost_ShouldReturnCreatedResponse()
    {
        var service = new Mock<IClubPostService>();
        service.Setup(s => s.CreateAsync(4, 7, "Organizer", "Update", "Details", PostType.Announcement, true))
            .ReturnsAsync(new ClubPost
            {
                Id = 10,
                ClubId = 4,
                UserId = 7,
                Title = "Update",
                Content = "Details",
                PostType = PostType.Announcement,
                IsPinned = true
            });

        var controller = CreateController(service.Object);

        var result = await controller.CreatePost(4, new ClubPostCreateRequest
        {
            Title = "Update",
            Content = "Details",
            PostType = PostType.Announcement,
            IsPinned = true
        });

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<ClubPostResponse>>().Subject;
        response.Data!.Title.Should().Be("Update");
        response.Data.IsPinned.Should().BeTrue();
    }

    [Fact]
    public async Task GetPosts_ShouldIncludeAuthenticatedUserContextWhenAvailable()
    {
        var service = new Mock<IClubPostService>();
        service.Setup(s => s.GetByClubIdAsync(4, 7, "Organizer", "meeting", PostSortBy.Recent, 2, 5))
            .ReturnsAsync((
                new List<ClubPost>
                {
                    new()
                    {
                        Id = 10,
                        ClubId = 4,
                        UserId = 7,
                        Title = "Weekly meeting",
                        Content = "Details"
                    }
                },
                1,
                "database",
                new Dictionary<int, UserListRecord>()));

        var controller = CreateController(service.Object);

        var result = await controller.GetPosts(4, "meeting", PostSortBy.Recent, 2, 5);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<ClubPostResponse>>>().Subject;
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Should().ContainSingle();
    }

    private static ClubPostController CreateController(IClubPostService service)
    {
        var controller = new ClubPostController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "organizer@example.com"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
