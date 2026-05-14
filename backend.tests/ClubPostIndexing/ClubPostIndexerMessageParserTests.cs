using backend.worker.clubpost_indexer;

using FluentAssertions;

using Xunit;

namespace backend.tests.ClubPostIndexing;

public class ClubPostIndexerMessageParserTests
{
    [Fact]
    public void Parse_ShouldReturnUpsertMessage_ForValidDocument()
    {
        var envelope = new ClubPostIndexerEnvelope(
            "clubpost-index-events",
            0,
            21,
            "9",
            """
            {
              "Id": 9,
              "ClubId": 7,
              "UserId": 5,
              "Title": "Welcome Back",
              "Content": "Weekly updates are here.",
              "PostType": "Announcement",
              "LikesCount": 3,
              "IsPinned": true,
              "CreatedAt": "2026-05-01T00:00:00Z",
              "UpdatedAt": "2026-05-02T00:00:00Z"
            }
            """,
            "upsert",
            new Dictionary<string, string?>());

        var message = ClubPostIndexerMessageParser.Parse(envelope);

        message.Operation.Should().Be(ClubPostIndexerOperation.Upsert);
        message.Document.Should().NotBeNull();
        message.Document!.Id.Should().Be(9);
        message.Document.PostType.Should().Be("Announcement");
    }

    [Fact]
    public void Parse_ShouldReturnDeleteMessage_ForValidDeletePayload()
    {
        var envelope = new ClubPostIndexerEnvelope(
            "clubpost-index-events",
            0,
            22,
            "9",
            """{ "PostId": 9 }""",
            "delete",
            new Dictionary<string, string?>());

        var message = ClubPostIndexerMessageParser.Parse(envelope);

        message.Operation.Should().Be(ClubPostIndexerOperation.Delete);
        message.PostId.Should().Be(9);
    }

    [Fact]
    public void Parse_ShouldRejectMissingOperation()
    {
        var envelope = new ClubPostIndexerEnvelope(
            "clubpost-index-events",
            0,
            23,
            "9",
            """{ "PostId": 9 }""",
            null,
            new Dictionary<string, string?>());

        var act = () => ClubPostIndexerMessageParser.Parse(envelope);

        act.Should()
            .Throw<ClubPostIndexerMessageParseException>()
            .WithMessage("CDC eventType header is required.");
    }
}
