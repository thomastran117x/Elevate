using backend.main.features.clubs;
using backend.main.features.clubs.discussions;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Clubs;

public class ClubDiscussionRepositoryTests
{
    private const int ClubId = 7;
    private const int OtherClubId = 8;
    private const int UserId = 11;

    [Fact]
    public async Task CreateAsync_ShouldPersistDiscussion_AndAssignAnId()
    {
        await using var harness = await Harness.CreateAsync();

        var created = await harness.Repository.CreateAsync(new ClubDiscussion
        {
            ClubId = ClubId,
            UserId = UserId,
            Title = "Weekend ride",
            Description = "Where should we go?"
        });

        created.Id.Should().BeGreaterThan(0);
        var stored = await harness.Repository.GetByIdAsync(created.Id);
        stored!.Title.Should().Be("Weekend ride");
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenTheDiscussionDoesNotExist()
    {
        await using var harness = await Harness.CreateAsync();

        (await harness.Repository.GetByIdAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldReturnNewestFirst_AndScopeToTheClub()
    {
        await using var harness = await Harness.CreateAsync();
        var baseTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        await harness.SeedAsync(ClubId, "Oldest", baseTime);
        await harness.SeedAsync(ClubId, "Newest", baseTime.AddDays(2));
        await harness.SeedAsync(ClubId, "Middle", baseTime.AddDays(1));
        await harness.SeedAsync(OtherClubId, "Other club", baseTime.AddDays(3));

        var discussions = await harness.Repository.GetByClubIdAsync(ClubId, 1, 20);

        discussions.Select(d => d.Title).Should().ContainInOrder("Newest", "Middle", "Oldest");
        discussions.Should().NotContain(d => d.Title == "Other club");
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldBreakTiesByIdDescending_WhenTimestampsMatch()
    {
        await using var harness = await Harness.CreateAsync();
        var sameTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        var first = await harness.SeedAsync(ClubId, "First", sameTime);
        var second = await harness.SeedAsync(ClubId, "Second", sameTime);

        var discussions = await harness.Repository.GetByClubIdAsync(ClubId, 1, 20);

        discussions.Select(d => d.Id).Should().ContainInOrder(second.Id, first.Id);
    }

    [Fact]
    public async Task GetByClubIdAsync_ShouldPaginate()
    {
        await using var harness = await Harness.CreateAsync();
        var baseTime = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        for (var index = 0; index < 5; index++)
            await harness.SeedAsync(ClubId, $"Topic {index}", baseTime.AddMinutes(index));

        var page2 = await harness.Repository.GetByClubIdAsync(ClubId, 2, 2);

        page2.Select(d => d.Title).Should().ContainInOrder("Topic 2", "Topic 1");
        (await harness.Repository.CountByClubIdAsync(ClubId)).Should().Be(5);
        (await harness.Repository.CountByClubIdAsync(OtherClubId)).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_ShouldOverwriteFieldsAndBumpUpdatedAt()
    {
        await using var harness = await Harness.CreateAsync();
        var created = await harness.SeedAsync(ClubId, "Old", new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));

        var updated = await harness.Repository.UpdateAsync(created.Id, new ClubDiscussion
        {
            Title = "New",
            Description = "New body"
        });

        updated!.Title.Should().Be("New");
        updated.Description.Should().Be("New body");
        updated.UpdatedAt.Should().BeAfter(created.UpdatedAt);
        updated.ClubId.Should().Be(ClubId);
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnNull_WhenTheDiscussionDoesNotExist()
    {
        await using var harness = await Harness.CreateAsync();

        var updated = await harness.Repository.UpdateAsync(999, new ClubDiscussion { Title = "X", Description = "Y" });

        updated.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveTheDiscussion_AndReportWhetherItExisted()
    {
        await using var harness = await Harness.CreateAsync();
        var created = await harness.SeedAsync(ClubId, "Doomed", new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));

        (await harness.Repository.DeleteAsync(created.Id)).Should().BeTrue();
        (await harness.Repository.GetByIdAsync(created.Id)).Should().BeNull();
        (await harness.Repository.DeleteAsync(created.Id)).Should().BeFalse();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public ClubDiscussionRepository Repository { get; }

        private Harness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
            Repository = new ClubDiscussionRepository(db);
        }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Users.Add(new User
            {
                Id = UserId,
                Email = "member@test.local",
                Usertype = "Participant"
            });
            db.Clubs.AddRange(
                NewClub(ClubId, "Discussion Club"),
                NewClub(OtherClubId, "Another Club"));
            await db.SaveChangesAsync();

            return new Harness(connection, db);
        }

        public async Task<ClubDiscussion> SeedAsync(int clubId, string title, DateTime createdAt)
        {
            var discussion = new ClubDiscussion
            {
                ClubId = clubId,
                UserId = UserId,
                Title = title,
                Description = $"{title} body",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            };

            Db.ClubDiscussions.Add(discussion);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return discussion;
        }

        private static Club NewClub(int id, string name) => new()
        {
            Id = id,
            UserId = UserId,
            Name = name,
            Description = "A club used for discussion repository tests.",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/discussion.png"
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
