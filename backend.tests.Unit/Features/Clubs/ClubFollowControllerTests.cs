using System.Security.Claims;

using backend.main.features.clubs.follow;
using backend.main.features.clubs.follow.contracts.responses;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs;

public class ClubFollowControllerTests
{
    [Fact]
    public async Task GetClubMembers_ShouldMapFollowResponses()
    {
        var service = new Mock<IFollowService>();
        service.Setup(s => s.GetFollowsByClubAsync(4, 1, 20))
            .ReturnsAsync([
                new FollowClub
                {
                    Id = 6,
                    ClubId = 4,
                    UserId = 22
                }
            ]);

        var controller = CreateClubController(service.Object);

        var result = await controller.GetClubMembers(4, 1, 20);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<FollowResponse>>>().Subject;
        response.Data.Should().ContainSingle();
        response.Data!.Single().UserId.Should().Be(22);
    }

    [Fact]
    public async Task CheckMembership_ShouldReturnMembershipStatus()
    {
        var service = new Mock<IFollowService>();
        service.Setup(s => s.IsMemberAsync(4, 7)).ReturnsAsync(true);

        var controller = CreateClubController(service.Object);

        var result = await controller.CheckMembership(4);

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        response.Data.Should().NotBeNull();
        response.Data!.ToString().Should().Contain("True");
    }

    private static ClubFollowController CreateClubController(IFollowService service)
    {
        var controller = new ClubFollowController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "member@example.com"),
                    new Claim(ClaimTypes.Role, "Participant")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
