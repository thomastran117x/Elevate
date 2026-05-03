using System.Text.Json;

using backend.main.configurations.resource.database;
using backend.main.dtos.messages;
using backend.main.models.core;
using backend.main.models.documents;
using backend.main.models.enums;
using backend.main.services.implementation;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace backend.test;

public class EventSearchOutboxWriterTests
{
    [Fact]
    public async Task StageUpsert_PersistsFullEventSearchPayload()
    {
        await using var context = CreateContext();
        var writer = new EventSearchOutboxWriter(context);

        writer.StageUpsert(new Events
        {
            Id = 42,
            ClubId = 9,
            Name = "Spring Mixer",
            Description = "Networking night",
            Location = "Downtown Hall",
            StartTime = new DateTime(2026, 5, 2, 18, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 2, 21, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 1, 13, 0, 0, DateTimeKind.Utc),
            Category = EventCategory.Other,
            VenueName = "Main Venue",
            City = "Toronto",
            Latitude = 43.6532,
            Longitude = -79.3832,
            Tags = ["social", "networking"],
            RegistrationCount = 17,
            isPrivate = true
        });

        await context.SaveChangesAsync();

        var row = await context.EventSearchOutbox.SingleAsync();
        row.AggregateType.Should().Be("event-index");
        row.AggregateId.Should().Be("42");
        row.Type.Should().Be("upsert");

        var payload = JsonSerializer.Deserialize<EventDocument>(
            row.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        payload.Should().NotBeNull();
        payload!.Id.Should().Be(42);
        payload.ClubId.Should().Be(9);
        payload.City.Should().Be("Toronto");
        payload.Tags.Should().Equal("social", "networking");
        payload.RegistrationCount.Should().Be(17);
        payload.LocationGeo.Should().NotBeNull();
    }

    [Fact]
    public async Task StageDelete_PersistsDeletePayloadWithEventId()
    {
        await using var context = CreateContext();
        var writer = new EventSearchOutboxWriter(context);

        writer.StageDelete(77);
        await context.SaveChangesAsync();

        var row = await context.EventSearchOutbox.SingleAsync();
        row.AggregateType.Should().Be("event-index");
        row.AggregateId.Should().Be("77");
        row.Type.Should().Be("delete");

        var payload = JsonSerializer.Deserialize<EventSearchDeletePayload>(
            row.Payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        payload.Should().Be(new EventSearchDeletePayload(77));
    }

    private static AppDatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDatabaseContext(options);
    }
}
