using backend.main.features.events.search;
using backend.worker.event_indexer;

using FluentAssertions;

namespace backend.tests.Unit.Workers.EventIndexing;

public class EventIndexerMessageParserTests
{
    [Fact]
    public void Parse_ShouldReturnUpsertMessage_ForValidDocument()
    {
        var envelope = new EventIndexerEnvelope(
            "event-index-events",
            0,
            12,
            "7",
            """
            {
              "Id": 7,
              "ClubId": 3,
              "Name": "Campus Run",
              "Description": "Weekly running session",
              "Location": "North Field",
              "Category": "Fitness",
              "StartTime": "2026-05-10T18:00:00Z",
              "EndTime": "2026-05-10T20:00:00Z",
              "CreatedAt": "2026-05-01T00:00:00Z",
              "UpdatedAt": "2026-05-02T00:00:00Z",
              "Tags": ["fitness", "community"],
              "RegistrationCount": 14
            }
            """,
            "upsert",
            new Dictionary<string, string?>());

        var message = EventIndexerMessageParser.Parse(envelope);

        message.Operation.Should().Be(EventIndexerOperation.Upsert);
        message.Document.Should().NotBeNull();
        message.Document!.Id.Should().Be(7);
        message.Document.ClubId.Should().Be(3);
    }

    [Fact]
    public void Parse_ShouldReturnDeleteMessage_ForValidDeletePayload()
    {
        var envelope = new EventIndexerEnvelope(
            "event-index-events",
            0,
            13,
            "7",
            """{ "EventId": 7 }""",
            "delete",
            new Dictionary<string, string?>());

        var message = EventIndexerMessageParser.Parse(envelope);

        message.Operation.Should().Be(EventIndexerOperation.Delete);
        message.EventId.Should().Be(7);
    }

    [Fact]
    public void Parse_ShouldRejectEndTimeEarlierThanStartTime()
    {
        var envelope = new EventIndexerEnvelope(
            "event-index-events",
            0,
            14,
            "7",
            """
            {
              "Id": 7,
              "ClubId": 3,
              "Name": "Campus Run",
              "Description": "Weekly running session",
              "Location": "North Field",
              "Category": "Fitness",
              "StartTime": "2026-05-10T20:00:00Z",
              "EndTime": "2026-05-10T18:00:00Z",
              "CreatedAt": "2026-05-01T00:00:00Z",
              "UpdatedAt": "2026-05-02T00:00:00Z",
              "RegistrationCount": 14
            }
            """,
            "upsert",
            new Dictionary<string, string?>());

        var act = () => EventIndexerMessageParser.Parse(envelope);

        act.Should()
            .Throw<EventIndexerMessageParseException>()
            .WithMessage("Event document endTime cannot be earlier than startTime.");
    }
}
