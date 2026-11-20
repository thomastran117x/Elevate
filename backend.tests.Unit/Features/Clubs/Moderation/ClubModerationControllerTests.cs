using System.Security.Claims;

using backend.main.features.clubs.moderation;
using backend.main.features.clubs.moderation.contracts.responses;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Clubs.Moderation;

public class ClubModerationControllerTests
{
    [Fact]
    public async Task GetModerationLog_ShouldReturnEnrichedEntries()
    {
        var log = new Mock<IClubModerationLog>();
        log.Setup(l => l.GetPageAsync(4, 7, "Organizer", 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([
                new ClubModerationAction
                {
                    Id = 1,
                    ClubId = 4,
                    ActorUserId = 7,
                    ActorClubRole = ClubModerationRoles.Owner,
                    Action = ClubModerationActionTypes.Remove,
                    ContentType = ClubModerationContentTypes.Comment,
                    ContentId = 30,
                    AuthorUserId = 8,
                    ContentSnapshot = "Buy now",
                    Reason = "Spam",
                    CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            ], 11));
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdsAsync(It.Is<IEnumerable<int>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { 7, 8 }))))
            .ReturnsAsync([
                new UserListRecord { Id = 7, Name = "Olive Owner", Username = "olive" },
                new UserListRecord { Id = 8, Name = "Sam Spammer", Username = "sam", Avatar = "a.png" },
            ]);

        var result = await CreateController(log.Object, users.Object).GetModerationLog(4, page: 2, pageSize: 10);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<ApiResponse<PagedResponse<ClubModerationLogItemResponse>>>().Subject;
        body.Data!.TotalCount.Should().Be(11);
        var item = body.Data.Items.Should().ContainSingle().Subject;
        item.Reason.Should().Be("Spam");
        item.ContentSnapshot.Should().Be("Buy now");
        item.ActorClubRole.Should().Be(ClubModerationRoles.Owner);
        item.Actor.Username.Should().Be("olive");
        item.Author.Name.Should().Be("Sam Spammer");
        item.Author.Avatar.Should().Be("a.png");
    }

    [Fact]
    public async Task GetModerationLog_ShouldClampPaging_AndSkipTheUserLookupWhenEmpty()
    {
        var log = new Mock<IClubModerationLog>();
        log.Setup(l => l.GetPageAsync(4, 7, "Organizer", 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0));
        var users = new Mock<IUserRepository>(MockBehavior.Strict);

        var result = await CreateController(log.Object, users.Object).GetModerationLog(4, page: 0, pageSize: 500);

        var body = ((OkObjectResult)result).Value.Should()
            .BeOfType<ApiResponse<PagedResponse<ClubModerationLogItemResponse>>>().Subject;
        body.Data!.Items.Should().BeEmpty();
    }

    private static ClubModerationController CreateController(IClubModerationLog log, IUserRepository users)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "7"),
                new Claim(ClaimTypes.Name, "owner@example.com"),
                new Claim(ClaimTypes.Role, "Organizer")
            ], "TestAuth");

        return new ClubModerationController(log, users)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }
}
