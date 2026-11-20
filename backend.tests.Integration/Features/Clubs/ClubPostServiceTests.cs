using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.moderation;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.staff;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;
using backend.tests.Integration.Infrastructure;

namespace backend.tests.Clubs;

public class ClubPostServiceTests
{
    [Fact]
    public async Task Volunteer_ShouldNotUpdateOrDeleteTheOwnersPost()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: false);
        var post = await harness.SeedPostAsync(userId: 7, title: "Owner post");

        var update = () => harness.Service.UpdateAsync(
            4, post.Id, 66, "Participant", "Volunteer updated", "Updated by volunteer", PostType.General, false);
        var delete = () => harness.Service.DeleteAsync(4, post.Id, 66, "Participant", "Outdated");

        await update.Should().ThrowAsync<ForbiddenException>().WithMessage(ClubPostService.UpdateForbiddenMessage);
        await delete.Should().ThrowAsync<ForbiddenException>().WithMessage(ClubPostService.DeleteForbiddenMessage);
        (await harness.Db.ClubPosts.AnyAsync(p => p.Id == post.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Volunteer_ShouldUpdateAndDeleteTheirOwnPost_WithoutModerationEntries()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: false);
        var post = await harness.SeedPostAsync(userId: 66, title: "Volunteer post");

        var updated = await harness.Service.UpdateAsync(
            4, post.Id, 66, "Participant", "Volunteer updated", "Updated by volunteer", PostType.General, false);
        await harness.Service.DeleteAsync(4, post.Id, 66, "Participant");

        updated.Title.Should().Be("Volunteer updated");
        (await harness.Db.ClubPosts.AnyAsync(p => p.Id == post.Id)).Should().BeFalse();
        (await harness.Db.ClubModerationActions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Manager_ShouldModerateTheOwnersPost_AndRecordEachActionWithTheChange()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: false);
        var post = await harness.SeedPostAsync(userId: 7, title: "Owner post");

        await harness.Service.UpdateAsync(
            4, post.Id, 55, "Participant", "Owner post", "content", PostType.General, true);
        await harness.Service.DeleteAsync(4, post.Id, 55, "Participant", "Event cancelled");

        (await harness.Db.ClubPosts.AnyAsync(p => p.Id == post.Id)).Should().BeFalse();
        var entries = await harness.Db.ClubModerationActions.AsNoTracking().OrderBy(entry => entry.Id).ToListAsync();
        entries.Select(entry => entry.Action).Should().Equal(ClubModerationActionTypes.Pin, ClubModerationActionTypes.Remove);
        entries.Should().OnlyContain(entry =>
            entry.ActorUserId == 55
            && entry.ActorClubRole == ClubModerationRoles.Manager
            && entry.AuthorUserId == 7
            && entry.ContentId == post.Id);
        entries[1].Reason.Should().Be("Event cancelled");
    }

    [Fact]
    public async Task PrivateClubPosts_ShouldAllowVolunteerButRejectUnrelatedUser()
    {
        await using var harness = await ClubPostServiceHarness.CreateAsync(isPrivate: true);
        await harness.SeedPostAsync(userId: 7, title: "Private owner post");

        var (items, totalCount, source, _) = await harness.Service.GetByClubIdAsync(
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
        private readonly PostgresTestDatabase _database;

        public AppDatabaseContext Db { get; }
        public ClubPostService Service { get; }

        private ClubPostServiceHarness(
            PostgresTestDatabase database,
            AppDatabaseContext db,
            ClubPostService service)
        {
            _database = database;
            Db = db;
            Service = service;
        }

        public static async Task<ClubPostServiceHarness> CreateAsync(bool isPrivate)
        {
            var database = await PostgresTestDatabase.CreateAsync();

            var db = database.CreateDbContext();

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

            var outboxWriter = new Mock<IClubPostSearchOutboxWriter>();

            var refreshCache = new Mock<IRefreshAheadCache>();
            refreshCache.Setup(c => c.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

            var userRepository = new Mock<IUserRepository>();
            userRepository
                .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<backend.main.features.profile.contracts.UserReadDetailLevel>()))
                .ReturnsAsync([]);

            // The production club rules over the seeded owner, manager and volunteer.
            var authorizer = TestResourceAuthorizers.Over(db);
            var service = new ClubPostService(
                db,
                new ClubPostRepository(db),
                new PostCommentRepository(db),
                authorizer,
                Mock.Of<IClubPostSearchService>(),
                outboxWriter.Object,
                userRepository.Object,
                refreshCache.Object,
                new ClubModerationLog(db, authorizer));

            return new ClubPostServiceHarness(database, db, service);
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
            await _database.DisposeAsync();
        }
    }
}

