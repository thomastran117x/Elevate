using backend.main.features.cache;
using backend.main.features.clubs.follow;
using backend.main.features.profile;

using FluentAssertions;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Clubs;

public class FollowServiceTests
{
    [Fact]
    public async Task IsMemberAsync_ShouldReturnFalseWhenMembershipIsNotFound()
    {
        var repository = new Mock<IFollowRepository>();
        var userRepository = new Mock<IUserRepository>();
        var cache = new Mock<ICacheService>();
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<FollowClub>(
                "follow:u:44:c:9",
                It.IsAny<Func<Task<FollowClub?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync((FollowClub?)null);

        var service = new FollowService(repository.Object, userRepository.Object, cache.Object, refreshCache.Object);

        var result = await service.IsMemberAsync(9, 44);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsMemberAsync_ShouldReturnTrueWhenMembershipIsCached()
    {
        var repository = new Mock<IFollowRepository>();
        var userRepository = new Mock<IUserRepository>();
        var cache = new Mock<ICacheService>();
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<FollowClub>(
                "follow:u:44:c:9",
                It.IsAny<Func<Task<FollowClub?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(new FollowClub { Id = 3, ClubId = 9, UserId = 44 });

        var service = new FollowService(repository.Object, userRepository.Object, cache.Object, refreshCache.Object);

        var result = await service.IsMemberAsync(9, 44);

        result.Should().BeTrue();
        repository.Verify(repo => repo.IsFollowingClubAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AddMembershipAsync_ShouldCacheMembershipAndInvalidateUserAndClubLists()
    {
        var server = Mock.Of<IServer>();
        var repository = new Mock<IFollowRepository>();
        var userRepository = new Mock<IUserRepository>();
        repository.Setup(repo => repo.FollowClubAsync(9, 44))
            .ReturnsAsync(new FollowClub { Id = 8, ClubId = 9, UserId = 44 });

        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetServer()).Returns(server);
        cache.Setup(service => service.ScanKeys(server, "follow:list:u:44:*"))
            .Returns(["follow:list:u:44:1:20"]);
        cache.Setup(service => service.ScanKeys(server, "follow:list:c:9:*"))
            .Returns(["follow:list:c:9:1:20"]);

        var refreshCache = new Mock<IRefreshAheadCache>();

        var service = new FollowService(repository.Object, userRepository.Object, cache.Object, refreshCache.Object);

        await service.AddMembershipAsync(9, 44);

        refreshCache.Verify(c => c.SetAsync(
                "follow:u:44:c:9",
                It.Is<FollowClub>(f => f.ClubId == 9),
                It.IsAny<TimeSpan>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()),
            Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:u:44:1:20"), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:c:9:1:20"), Times.Once);
    }

    [Fact]
    public async Task RemoveMembershipAsync_ShouldRemoveFollowKeyAndInvalidateLists()
    {
        var server = Mock.Of<IServer>();
        var repository = new Mock<IFollowRepository>();
        var userRepository = new Mock<IUserRepository>();
        var cache = new Mock<ICacheService>();
        cache.Setup(service => service.GetServer()).Returns(server);
        cache.Setup(service => service.ScanKeys(server, "follow:list:u:44:*"))
            .Returns(["follow:list:u:44:1:20"]);
        cache.Setup(service => service.ScanKeys(server, "follow:list:c:9:*"))
            .Returns(["follow:list:c:9:1:20"]);

        var refreshCache = new Mock<IRefreshAheadCache>();

        var service = new FollowService(repository.Object, userRepository.Object, cache.Object, refreshCache.Object);

        await service.RemoveMembershipAsync(9, 44);

        repository.Verify(repo => repo.UnfollowClubAsync(9, 44), Times.Once);
        refreshCache.Verify(c => c.RemoveAsync("follow:u:44:c:9"), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:u:44:1:20"), Times.Once);
        cache.Verify(service => service.DeleteKeyAsync("follow:list:c:9:1:20"), Times.Once);
    }
}
