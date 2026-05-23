using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs.follow;

using FluentAssertions;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Clubs;

public class FollowServiceTests
{
    [Fact]
    public async Task IsMemberAsync_ShouldCacheMissingMembershipAsNullSentinel()
    {
        var repository = new Mock<IFollowRepository>();
        repository.Setup(repo => repo.IsFollowingClubAsync(9, 44))
            .ReturnsAsync((FollowClub?)null);

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetValueAsync("follow:u:44:c:9"))
            .ReturnsAsync((string?)null);

        var service = new FollowService(repository.Object, cache.Object);

        var result = await service.IsMemberAsync(9, 44);

        result.Should().BeFalse();
        cache.Verify(service => service.SetValueAsync(
            "follow:u:44:c:9",
            "__null__",
            It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    [Fact]
    public async Task IsMemberAsync_ShouldReturnCachedMembershipWithoutRepositoryLookup()
    {
        var repository = new Mock<IFollowRepository>();
        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetValueAsync("follow:u:44:c:9"))
            .ReturnsAsync(JsonSerializer.Serialize(new FollowClub
            {
                Id = 3,
                ClubId = 9,
                UserId = 44
            }));

        var service = new FollowService(repository.Object, cache.Object);

        var result = await service.IsMemberAsync(9, 44);

        result.Should().BeTrue();
        repository.Verify(repo => repo.IsFollowingClubAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AddMembershipAsync_ShouldCacheMembershipAndInvalidateUserAndClubLists()
    {
        var server = Mock.Of<IServer>();
        var repository = new Mock<IFollowRepository>();
        repository.Setup(repo => repo.FollowClubAsync(9, 44))
            .ReturnsAsync(new FollowClub
            {
                Id = 8,
                ClubId = 9,
                UserId = 44
            });

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetServer()).Returns(server);
        cache.Setup(service => service.ScanKeys(server, "follow:list:u:44:*"))
            .Returns(["follow:list:u:44:1:20"]);
        cache.Setup(service => service.ScanKeys(server, "follow:list:c:9:*"))
            .Returns(["follow:list:c:9:1:20"]);

        var service = new FollowService(repository.Object, cache.Object);

        await service.AddMembershipAsync(9, 44);

        cache.Verify(service => service.SetValueAsync(
            "follow:u:44:c:9",
            It.Is<string>(payload => payload.Contains("\"ClubId\":9")),
            It.IsAny<TimeSpan?>()),
            Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:u:44:1:20"), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:c:9:1:20"), Times.Once);
    }

    [Fact]
    public async Task RemoveMembershipAsync_ShouldDeleteFollowKeyAndInvalidateLists()
    {
        var server = Mock.Of<IServer>();
        var repository = new Mock<IFollowRepository>();
        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetServer()).Returns(server);
        cache.Setup(service => service.ScanKeys(server, "follow:list:u:44:*"))
            .Returns(["follow:list:u:44:1:20"]);
        cache.Setup(service => service.ScanKeys(server, "follow:list:c:9:*"))
            .Returns(["follow:list:c:9:1:20"]);

        var service = new FollowService(repository.Object, cache.Object);

        await service.RemoveMembershipAsync(9, 44);

        repository.Verify(repo => repo.UnfollowClubAsync(9, 44), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:u:44:c:9"), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:u:44:1:20"), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:c:9:1:20"), Times.Once);
    }
}
