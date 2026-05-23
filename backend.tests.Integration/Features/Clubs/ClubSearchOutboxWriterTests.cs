using System.Text.Json;

using backend.main.features.clubs;
using backend.main.features.clubs.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace backend.tests.Clubs;

public class ClubSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_ShouldPersistClubDocumentPayload()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDatabaseContext(options);
        await context.Database.EnsureCreatedAsync();

        var writer = new ClubSearchOutboxWriter(context);
        var club = new Club
        {
            Id = 7,
            UserId = 2,
            Name = "Campus Chess Club",
            Description = "Weekly games",
            Clubtype = ClubType.Social,
            ClubImage = "club.png",
            MemberCount = 18,
            Location = "Student Center",
            CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        writer.StageUpsert(club);
        await context.SaveChangesAsync();

        var outbox = await context.ClubSearchOutbox.SingleAsync();
        outbox.AggregateType.Should().Be("club-index");
        outbox.Type.Should().Be("upsert");

        var payload = JsonSerializer.Deserialize<ClubDocument>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.Id.Should().Be(7);
        payload.Name.Should().Be("Campus Chess Club");
        payload.Location.Should().Be("Student Center");
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

        var writer = new ClubSearchOutboxWriter(context);

        writer.StageDelete(19);
        await context.SaveChangesAsync();

        var outbox = await context.ClubSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<ClubSearchDeletePayload>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.ClubId.Should().Be(19);
    }
}
