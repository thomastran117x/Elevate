using backend.main.models.documents;

using Confluent.Kafka;

using FluentAssertions;

using Xunit;

namespace backend.test.Workers;

public class EventIndexerMessageParserTests
{
    [Fact]
    public void Parse_UpsertHeader_WithValidDocument_ReturnsUpsertMessage()
    {
        var envelope = CreateEnvelope(
            "upsert",
            """
            {
              "id": 42,
              "clubId": 9,
              "name": "Spring Mixer",
              "description": "Networking night",
              "location": "Downtown Hall",
              "isPrivate": true,
              "startTime": "2026-05-02T18:00:00Z",
              "endTime": "2026-05-02T21:00:00Z",
              "createdAt": "2026-05-01T12:00:00Z",
              "updatedAt": "2026-05-01T13:00:00Z",
              "category": "Other",
              "venueName": "Main Venue",
              "city": "Toronto",
              "tags": ["social", "networking"],
              "registrationCount": 17
            }
            """
        );

        var parsed = backend.worker.event_indexer.EventIndexerMessageParser.Parse(envelope);

        parsed.Operation.Should().Be(backend.worker.event_indexer.EventIndexerOperation.Upsert);
        parsed.EventId.Should().Be(42);
        parsed.Document.Should().BeEquivalentTo(new EventDocument
        {
            Id = 42,
            ClubId = 9,
            Name = "Spring Mixer",
            Description = "Networking night",
            Location = "Downtown Hall",
            IsPrivate = true,
            StartTime = new DateTime(2026, 5, 2, 18, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 5, 2, 21, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 1, 13, 0, 0, DateTimeKind.Utc),
            Category = "Other",
            VenueName = "Main Venue",
            City = "Toronto",
            Tags = ["social", "networking"],
            RegistrationCount = 17
        }, options => options.Excluding(d => d.LocationGeo));
    }

    [Fact]
    public void Parse_DeleteHeader_WithValidPayload_ReturnsDeleteMessage()
    {
        var envelope = CreateEnvelope("delete", """{ "eventId": 77 }""");

        var parsed = backend.worker.event_indexer.EventIndexerMessageParser.Parse(envelope);

        parsed.Operation.Should().Be(backend.worker.event_indexer.EventIndexerOperation.Delete);
        parsed.EventId.Should().Be(77);
        parsed.Document.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingHeader_ThrowsParseException()
    {
        var envelope = CreateEnvelope(null, """{ "eventId": 77 }""");

        var act = () => backend.worker.event_indexer.EventIndexerMessageParser.Parse(envelope);

        act.Should()
            .Throw<backend.worker.event_indexer.EventIndexerMessageParseException>()
            .WithMessage("*eventType header is required*");
    }

    [Fact]
    public void Parse_UnsupportedHeader_ThrowsParseException()
    {
        var envelope = CreateEnvelope("merge", """{ "eventId": 77 }""");

        var act = () => backend.worker.event_indexer.EventIndexerMessageParser.Parse(envelope);

        act.Should()
            .Throw<backend.worker.event_indexer.EventIndexerMessageParseException>()
            .WithMessage("*not supported*");
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsParseException()
    {
        var envelope = CreateEnvelope("upsert", """{ "id": """);

        var act = () => backend.worker.event_indexer.EventIndexerMessageParser.Parse(envelope);

        act.Should()
            .Throw<backend.worker.event_indexer.EventIndexerMessageParseException>()
            .WithMessage("*Invalid upsert payload JSON*");
    }

    private static backend.worker.event_indexer.EventIndexerEnvelope CreateEnvelope(
        string? operation,
        string payload)
    {
        var headers = new Headers();
        if (operation != null)
            headers.Add("eventType", System.Text.Encoding.UTF8.GetBytes(operation));

        var result = new ConsumeResult<string, string>
        {
            Topic = "event-index-events",
            Partition = new Partition(0),
            Offset = new Offset(12),
            Message = new Message<string, string>
            {
                Key = "event-42",
                Value = payload,
                Headers = headers
            }
        };

        return backend.worker.event_indexer.EventIndexerEnvelope.FromConsumeResult(result);
    }
}
