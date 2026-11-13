using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.moderation;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.staff;
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
    private const int OwnerId = 7;
    private const int StaffId = 55;
    private const int ViewerId = 88;

    private static ClubPostService CreateService(
        AppDatabaseContext db,
        IRefreshAheadCache? cache = null,
        IUserRepository? userRepository = null)
    {
        return new ClubPostService(
            db,
            Mock.Of<IClubPostRepository>(),
            new PostCommentRepository(db),
            // The production club rules, over the club each test seeds.
            TestResourceAuthorizers.Over(db),
            Mock.Of<IClubPostSearchService>(),
            Mock.Of<IClubPostSearchOutboxWriter>(),
            userRepository ?? DefaultUserRepository(),
            cache ?? Mock.Of<IRefreshAheadCache>(),
            Mock.Of<IClubModerationLog>());
    }

    private static IUserRepository DefaultUserRepository()
    {
        var mock = new Mock<IUserRepository>();
        mock.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<UserReadDetailLevel>()))
            .ReturnsAsync([]);
        return mock.Object;
    }

    private static async Task SeedClubAsync(AppDatabaseContext db, bool isPrivate, int id = 4, bool withStaff = false)
    {
        db.Users.AddRange(
            new User { Id = OwnerId, Email = "owner@test.local", Usertype = "Organizer" },
            new User { Id = StaffId, Email = "staff@test.local", Usertype = "Participant" });
        db.Clubs.Add(new Club
        {
            Id = id,
            UserId = OwnerId,
            Name = isPrivate ? "Secret Club" : "Chess Club",
            Description = isPrivate ? "Hidden" : "Strategy",
            Clubtype = isPrivate ? ClubType.Social : ClubType.Academic,
            ClubImage = "img.png",
            isPrivate = isPrivate
        });
        if (withStaff)
        {
            db.ClubStaff.Add(new ClubStaff
            {
                ClubId = id,
                UserId = StaffId,
                Role = ClubStaffRole.Volunteer,
                GrantedByUserId = OwnerId
            });
        }

        await db.SaveChangesAsync();
    }

    private static ClubPost Post(int id, int clubId) => new()
    {
        Id = id,
        ClubId = clubId,
        UserId = OwnerId,
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
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: false);

        var post = Post(11, clubId: 4);
        var service = CreateService(db, cache: CacheReturning(post));

        var (result, _) = await service.GetByIdAsync(4, 11, null, null);

        result.Id.Should().Be(11);
        result.Title.Should().Be("Hello");
    }

    [Fact]
    public async Task GetByIdAsync_IncludesAuthorInfo_WhenUserFound()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: false);

        var userRepository = new Mock<IUserRepository>();
        userRepository
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<UserReadDetailLevel>()))
            .ReturnsAsync([new UserListRecord { Id = 7, Email = "a@b.com", Username = "author", Name = "Alice" }]);

        var service = CreateService(db, CacheReturning(Post(11, 4)), userRepository: userRepository.Object);

        var (_, author) = await service.GetByIdAsync(4, 11, null, null);

        author.Should().NotBeNull();
        author!.Name.Should().Be("Alice");
        author.Username.Should().Be("author");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsResourceNotFound_WhenCacheReturnsNull()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: false);

        var service = CreateService(db, cache: CacheReturning(null));

        var act = () => service.GetByIdAsync(4, 11, null, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("*11*");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsResourceNotFound_WhenPostBelongsToDifferentClub()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: false);

        var service = CreateService(db, cache: CacheReturning(Post(11, clubId: 99)));

        var act = () => service.GetByIdAsync(4, 11, null, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("*11*");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsResourceNotFound_WhenClubDoesNotExist()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();

        var service = CreateService(db, cache: CacheReturning(Post(11, 4)));

        var act = () => service.GetByIdAsync(4, 11, null, null);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Club 4 not found");
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsUnauthorized_WhenPrivateClubAndAnonymous()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: true);

        var service = CreateService(db);

        var act = () => service.GetByIdAsync(4, 11, requestingUserId: null, requestingUserRole: null);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsForbidden_WhenPrivateClubAndUserNotMember()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: true);

        var service = CreateService(db);

        var act = () => service.GetByIdAsync(4, 11, requestingUserId: ViewerId, requestingUserRole: "Participant");

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("*member*");
    }

    [Fact]
    public async Task GetByIdAsync_AllowsStaff_WhenPrivateClub()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = database.CreateDbContext();
        await SeedClubAsync(db, isPrivate: true, withStaff: true);

        var service = CreateService(db, cache: CacheReturning(Post(11, 4)));

        var (result, _) = await service.GetByIdAsync(4, 11, requestingUserId: StaffId, requestingUserRole: "Participant");

        result.Id.Should().Be(11);
    }
}
