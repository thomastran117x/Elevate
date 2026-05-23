using System.Text.Json;

using backend.main.features.events.search;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Integration.Features.Events;

public class EventSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_ShouldPersistEventDocumentPayload()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDatabaseContext(options);
        await context.Database.EnsureCreatedAsync();

        var writer = new EventSearchOutboxWriter(context);
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
        await context.SaveChangesAsync();

        var outbox = await context.EventSearchOutbox.SingleAsync();
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
    public async Task StageDelete_ShouldPersistDeletePayload()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AppDatabaseContext(options);
        await context.Database.EnsureCreatedAsync();

        var writer = new EventSearchOutboxWriter(context);

        writer.StageDelete(27);
        await context.SaveChangesAsync();

        var outbox = await context.EventSearchOutbox.SingleAsync();
        outbox.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<EventSearchDeletePayload>(outbox.Payload);
        payload.Should().NotBeNull();
        payload!.EventId.Should().Be(27);
    }
}
