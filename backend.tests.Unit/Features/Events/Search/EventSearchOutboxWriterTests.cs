using System.Text.Json;

using backend.main.features.events.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events.Search;

public class EventSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_ShouldPersistEventDocumentPayload()
    {
        await using var harness = await EventSearchOutboxWriterHarness.CreateAsync();
        var writer = new EventSearchOutboxWriter(harness.Db);
        var ev = new EventEntity
        {
            Id = 15,
            ClubId = 4,
            Name = "Open Gym Night",
            Description = "All welcome.",
            Location = "Campus Rec Center",
            Category = backend.main.features.events.EventCategory.Fitness,
            StartTime = new DateTime(2026, 5, 10, 18, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 10, 20, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            Tags = ["fitness", "social"],
            RegistrationCount = 21
        };

        writer.StageUpsert(ev);
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.EventSearchOutbox.SingleAsync();
        outbox.AggregateType.Should().Be("event-index");
        outbox.Type.Should().Be("upsert");

        var payload = JsonSerializer.Deserialize<EventDocument>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.Id.Should().Be(15);
        payload.ClubId.Should().Be(4);
        payload.Name.Should().Be("Open Gym Night");
        payload.Location.Should().Be("Campus Rec Center");
    }

    [Fact]
    public async Task StageSync_ShouldWriteDelete_ForNonPublicLifecycle()
    {
        await using var harness = await EventSearchOutboxWriterHarness.CreateAsync();
        var writer = new EventSearchOutboxWriter(harness.Db);
        var ev = new EventEntity
        {
            Id = 27,
            LifecycleState = backend.main.features.events.EventLifecycleState.Draft,
            Name = "Draft Event",
            Description = "Draft description",
            Location = "Room 1",
            Category = backend.main.features.events.EventCategory.Other
        };

        writer.StageSync(ev);
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.EventSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");
        var payload = JsonSerializer.Deserialize<EventSearchDeletePayload>(outbox.Payload);
        payload!.EventId.Should().Be(27);
    }

    [Fact]
    public async Task StageDelete_ShouldPersistDeletePayload()
    {
        await using var harness = await EventSearchOutboxWriterHarness.CreateAsync();
        var writer = new EventSearchOutboxWriter(harness.Db);

        writer.StageDelete(27);
        await harness.Db.SaveChangesAsync();

        var outbox = await harness.Db.EventSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<EventSearchDeletePayload>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.EventId.Should().Be(27);
    }

    private sealed class EventSearchOutboxWriterHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public AppDatabaseContext Db { get; }

        private EventSearchOutboxWriterHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<EventSearchOutboxWriterHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();
            return new EventSearchOutboxWriterHarness(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
