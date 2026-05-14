using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.staff;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;

namespace backend.tests.Clubs;

public class ClubPostServiceTests
{
    [Fact]
    public async Task Volunteer_ShouldBeAbleToUpdateAndDeleteClubPosts()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: false);
        var post = await harness.SeedPostAsync(userId: 7, title: "Owner post");

        var updated = await harness.Service.UpdateAsync(
            4,
            post.Id,
            66,
            "Participant",
            "Volunteer updated",
            "Updated by volunteer",
            PostType.General,
            false);

        updated.Title.Should().Be("Volunteer updated");

        await harness.Service.DeleteAsync(4, post.Id, 66, "Participant");

        (await harness.Db.ClubPosts.AnyAsync(p => p.Id == post.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task PrivateClubPosts_ShouldAllowVolunteerButRejectUnrelatedUser()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: true);
        await harness.SeedPostAsync(userId: 7, title: "Private owner post");

        var (items, totalCount, source) = await harness.Service.GetByClubIdAsync(
            4,
            66,
            "Participant",
            null,
            PostSortBy.Recent,
            1,
            20);

        totalCount.Should().Be(1);
        items.Should().ContainSingle();
        source.Should().NotBeNullOrWhiteSpace();

        var act = () => harness.Service.GetByClubIdAsync(
            4,
            88,
            "Participant",
            null,
            PostSortBy.Recent,
            1,
            20);

        await act.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("You must be a member of this club to view its posts.");
    }

    private sealed class ClubPostServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public ClubPostService Service { get; }

        private ClubPostServiceHarness(
            SqliteConnection connection,
            AppDatabaseContext db,
            ClubPostService service)
        {
            _connection = connection;
            Db = db;
            Service = service;
        }

        public static async Task<ClubPostServiceHarness> CreateAsync(bool isPrivate)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var dbOptions = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(dbOptions);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                new User { Id = 7, Email = "owner@test.local", Usertype = "Organizer" },
                new User { Id = 55, Email = "manager@test.local", Usertype = "Participant" },
                new User { Id = 66, Email = "volunteer@test.local", Usertype = "Participant" },
                new User { Id = 88, Email = "viewer@test.local", Usertype = "Participant" });
            db.Clubs.Add(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Games Club",
                Description = "A club for tabletop and social games.",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/games.png",
                isPrivate = isPrivate
            });
            db.ClubStaff.Add(new ClubStaff
            {
                ClubId = 4,
                UserId = 55,
                Role = ClubStaffRole.Manager,
                GrantedByUserId = 7
            });
            db.ClubStaff.Add(new ClubStaff
            {
                ClubId = 4,
                UserId = 66,
                Role = ClubStaffRole.Volunteer,
                GrantedByUserId = 7
            });
            await db.SaveChangesAsync();

            var club = await db.Clubs.SingleAsync(c => c.Id == 4);

            var clubService = new Mock<IClubService>();
            clubService.Setup(service => service.GetClub(4))
                .ReturnsAsync(club);
            clubService.Setup(service => service.CanManageClubAsync(4, 55, "Participant"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.CanManageClubAsync(4, 7, "Organizer"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.CanManageClubAsync(4, 88, "Participant"))
                .ReturnsAsync(false);
            clubService.Setup(service => service.CanManageClubPostsAsync(4, 55, "Participant"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.CanManageClubPostsAsync(4, 66, "Participant"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.CanManageClubPostsAsync(4, 7, "Organizer"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.CanManageClubPostsAsync(4, 88, "Participant"))
                .ReturnsAsync(false);
            clubService.Setup(service => service.HasClubStaffAccessAsync(4, 55, "Participant"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.HasClubStaffAccessAsync(4, 66, "Participant"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.HasClubStaffAccessAsync(4, 7, "Organizer"))
                .ReturnsAsync(true);
            clubService.Setup(service => service.HasClubStaffAccessAsync(4, 88, "Participant"))
                .ReturnsAsync(false);

            var outboxWriter = new Mock<IClubPostSearchOutboxWriter>();

            var service = new ClubPostService(
                db,
                new ClubPostRepository(db),
                clubService.Object,
                new FollowRepository(db),
                Mock.Of<IClubPostSearchService>(),
                outboxWriter.Object);

            return new ClubPostServiceHarness(connection, db, service);
        }

        public async Task<ClubPost> SeedPostAsync(int userId, string title)
        {
            var post = new ClubPost
            {
                ClubId = 4,
                UserId = userId,
                Title = title,
                Content = "content",
                PostType = PostType.General,
                IsPinned = false
            };

            Db.ClubPosts.Add(post);
            await Db.SaveChangesAsync();
            return post;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
