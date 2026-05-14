using backend.main.features.clubs.search;
using backend.worker.club_indexer;

using FluentAssertions;

using Xunit;

namespace backend.tests.ClubIndexing;

public class ClubIndexerMessageParserTests
{
    [Fact]
    public void Parse_ShouldReturnUpsertMessage_ForValidDocument()
    {
        var envelope = new ClubIndexerEnvelope(
            "club-index-events",
            0,
            12,
            "7",
            """
            {
              "Id": 7,
              "Name": "Campus Chess Club",
              "Description": "Weekly strategy nights",
              "ClubType": "Social",
              "MemberCount": 22,
              "IsPrivate": false,
              "CreatedAt": "2026-05-01T00:00:00Z",
              "UpdatedAt": "2026-05-02T00:00:00Z"
            }
            """,
            "upsert",
            new Dictionary<string, string?>());

        var message = ClubIndexerMessageParser.Parse(envelope);

        message.Operation.Should().Be(ClubIndexerOperation.Upsert);
        message.Document.Should().NotBeNull();
        message.Document!.Id.Should().Be(7);
    }

    [Fact]
    public void Parse_ShouldReturnDeleteMessage_ForValidDeletePayload()
    {
        var envelope = new ClubIndexerEnvelope(
            "club-index-events",
            0,
            13,
            "7",
            """{ "ClubId": 7 }""",
            "delete",
            new Dictionary<string, string?>());

        var message = ClubIndexerMessageParser.Parse(envelope);

        message.Operation.Should().Be(ClubIndexerOperation.Delete);
        message.ClubId.Should().Be(7);
    }

    [Fact]
    public void Parse_ShouldRejectInvalidRating()
    {
        var envelope = new ClubIndexerEnvelope(
            "club-index-events",
            0,
            14,
            "7",
            """
            {
              "Id": 7,
              "Name": "Campus Chess Club",
              "Description": "Weekly strategy nights",
              "ClubType": "Social",
              "MemberCount": 22,
              "Rating": 7.5,
              "IsPrivate": false,
              "CreatedAt": "2026-05-01T00:00:00Z",
              "UpdatedAt": "2026-05-02T00:00:00Z"
            }
            """,
            "upsert",
            new Dictionary<string, string?>());

        var act = () => ClubIndexerMessageParser.Parse(envelope);

        act.Should()
            .Throw<ClubIndexerMessageParseException>()
            .WithMessage("Club document rating must be between 0 and 5.");
    }
}
