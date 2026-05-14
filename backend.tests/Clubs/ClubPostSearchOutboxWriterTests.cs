using System.Text.Json;

using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace backend.tests.Clubs;

public class ClubPostSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_ShouldPersistClubPostDocumentPayload()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDatabaseContext(options);
        await context.Database.EnsureCreatedAsync();

        var writer = new ClubPostSearchOutboxWriter(context);
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
        await context.SaveChangesAsync();

        var outbox = await context.ClubPostSearchOutbox.SingleAsync();
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
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDatabaseContext(options);
        await context.Database.EnsureCreatedAsync();

        var writer = new ClubPostSearchOutboxWriter(context);

        writer.StageDelete(9);
        await context.SaveChangesAsync();

        var outbox = await context.ClubPostSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<ClubPostSearchDeletePayload>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.PostId.Should().Be(9);
    }
}
