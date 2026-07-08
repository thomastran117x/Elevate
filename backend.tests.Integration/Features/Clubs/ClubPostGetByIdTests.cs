using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Moq;

namespace backend.tests.Clubs;

public class ClubPostGetByIdTests
{
    private static ClubPostService CreateService(
        AppDatabaseContext db,
        IClubService? clubService = null,
        IRefreshAheadCache? cache = null,
        IFollowRepository? followRepository = null,
        IUserRepository? userRepository = null)
    {
        return new ClubPostService(
            db,
            Mock.Of<IClubPostRepository>(),
            clubService ?? Mock.Of<IClubService>(),
            followRepository ?? Mock.Of<IFollowRepository>(),
            Mock.Of<IClubPostSearchService>(),
            Mock.Of<IClubPostSearchOutboxWriter>(),
            userRepository ?? DefaultUserRepository(),
            cache ?? Mock.Of<IRefreshAheadCache>());
    }

    private static IUserRepository DefaultUserRepository()
    {
        var mock = new Mock<IUserRepository>();
        mock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<UserReadDetailLevel>()))
            .ReturnsAsync([]);
        return mock.Object;
    }

    private static Club PublicClub(int id = 4) => new()
    {
        Id = id,
        UserId = 7,
        Name = "Chess Club",
        Description = "Strategy",
        Clubtype = ClubType.Academic,
        ClubImage = "img.png",
        isPrivate = false
    };

    private static Club PrivateClub(int id = 4) => new()
    {
        Id = id,
        UserId = 7,
        Name = "Secret Club",
        Description = "Hidden",
        Clubtype = ClubType.Social,
        ClubImage = "img.png",
        isPrivate = true
    };

    private static ClubPost Post(int id, int clubId) => new()
    {
        Id = id,
        ClubId = clubId,
        UserId = 7,
        Title = "Hello",
        Content = "World"
    };

    private static IRefreshAheadCache CacheReturning(ClubPost? post)
    {
        var mock = new Mock<IRefreshAheadCache>();
        mock.Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<ClubPost?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(post);
        return mock.Object;
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsPost_WhenCacheHits()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PublicClub());

        var post = Post(11, clubId: 4);
        var service = CreateService(db, clubService.Object, cache: CacheReturning(post));

        var (result, _) = await service.GetByIdAsync(4, 11, null, null);

        result.Id.Should().Be(11);
        result.Title.Should().Be("Hello");
    }

    [Fact]
    public async Task GetByIdAsync_IncludesAuthorInfo_WhenUserFound()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PublicClub());

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<UserReadDetailLevel>()))
            .ReturnsAsync([new UserListRecord { Id = 7, Email = "a@b.com", Username = "author", Name = "Alice" }]);

        var service = CreateService(db, clubService.Object, CacheReturning(Post(11, 4)), userRepository: userRepository.Object);

        var (_, author) = await service.GetByIdAsync(4, 11, null, null);

        author.Should().NotBeNull();
        author!.Name.Should().Be("Alice");
        author.Username.Should().Be("author");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsResourceNotFound_WhenCacheReturnsNull()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PublicClub());

        var service = CreateService(db, clubService.Object, cache: CacheReturning(null));

        var act = () => service.GetByIdAsync(4, 11, null, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("*11*");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsResourceNotFound_WhenPostBelongsToDifferentClub()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PublicClub(4));

        var service = CreateService(db, clubService.Object, cache: CacheReturning(Post(11, clubId: 99)));

        var act = () => service.GetByIdAsync(4, 11, null, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("*11*");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsUnauthorized_WhenPrivateClubAndAnonymous()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PrivateClub());

        var service = CreateService(db, clubService.Object);

        var act = () => service.GetByIdAsync(4, 11, requestingUserId: null, requestingUserRole: null);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsForbidden_WhenPrivateClubAndUserNotMember()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PrivateClub());
        clubService.Setup(s => s.HasClubStaffAccessAsync(4, 88, "Participant")).ReturnsAsync(false);

        var followRepository = new Mock<IFollowRepository>();
        followRepository.Setup(r => r.IsFollowingClubAsync(4, 88)).ReturnsAsync((FollowClub?)null);

        var service = CreateService(db, clubService.Object, followRepository: followRepository.Object);

        var act = () => service.GetByIdAsync(4, 11, requestingUserId: 88, requestingUserRole: "Participant");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*member*");
    }

    [Fact]
    public async Task GetByIdAsync_AllowsStaff_WhenPrivateClub()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var clubService = new Mock<IClubService>();
        clubService.Setup(s => s.GetClub(4)).ReturnsAsync(PrivateClub());
        clubService.Setup(s => s.HasClubStaffAccessAsync(4, 55, "Participant")).ReturnsAsync(true);

        var service = CreateService(db, clubService.Object, cache: CacheReturning(Post(11, 4)));

        var (result, _) = await service.GetByIdAsync(4, 11, requestingUserId: 55, requestingUserRole: "Participant");

        result.Id.Should().Be(11);
    }
}
