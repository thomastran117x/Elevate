using System.Text.Json;

using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Clubs.Posts.Search;

public class ClubPostSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_ShouldPersistClubPostDocumentPayload()
    {
        await using var harness = await ClubPostSearchOutboxWriterHarness.CreateAsync();
        var writer = new ClubPostSearchOutboxWriter(harness.Db);
        var post = new ClubPost
        {
            Id = 9,
            ClubId = 7,
            UserId = 5,
            Title = "Welcome Back",
            Content = "Weekly updates are here.",
            PostType = PostType.Announcement,
            LikesCount = 3,
            IsPinned = true,
            CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        writer.StageUpsert(post);
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.ClubPostSearchOutbox.SingleAsync();
        outbox.AggregateType.Should().Be("clubpost-index");
        outbox.Type.Should().Be("upsert");

        var payload = JsonSerializer.Deserialize<ClubPostDocument>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.Id.Should().Be(9);
        payload.Title.Should().Be("Welcome Back");
        payload.PostType.Should().Be("Announcement");
    }

    [Fact]
    public async Task StageDelete_ShouldPersistDeletePayload()
    {
        await using var harness = await ClubPostSearchOutboxWriterHarness.CreateAsync();
        var writer = new ClubPostSearchOutboxWriter(harness.Db);

        writer.StageDelete(9);
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.ClubPostSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<ClubPostSearchDeletePayload>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.PostId.Should().Be(9);
    }

    private sealed class ClubPostSearchOutboxWriterHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDatabaseContext Db { get; }

        private ClubPostSearchOutboxWriterHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<ClubPostSearchOutboxWriterHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();
            return new ClubPostSearchOutboxWriterHarness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
