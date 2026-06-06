using System.Text.Json;

using backend.main.features.clubs;
using backend.main.features.clubs.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Clubs.Search;

public class ClubSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_ShouldPersistClubDocumentPayload()
    {
        await using var harness = await ClubSearchOutboxWriterHarness.CreateAsync();
        var writer = new ClubSearchOutboxWriter(harness.Db);
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
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.ClubSearchOutbox.SingleAsync();
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
        await using var harness = await ClubSearchOutboxWriterHarness.CreateAsync();
        var writer = new ClubSearchOutboxWriter(harness.Db);

        writer.StageDelete(19);
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.ClubSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<ClubSearchDeletePayload>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.ClubId.Should().Be(19);
    }

    private sealed class ClubSearchOutboxWriterHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDatabaseContext Db { get; }

        private ClubSearchOutboxWriterHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<ClubSearchOutboxWriterHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();
            return new ClubSearchOutboxWriterHarness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
