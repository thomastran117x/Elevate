using backend.main.features.clubs.discussions.replies;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs;

public class DiscussionReplyCursorCodecTests
{
    [Fact]
    public void EncodeAndDecode_ShouldRoundTripTimestampAndId()
    {
        var reply = new ClubDiscussionReply
        {
            Id = 42,
            CreatedAt = new DateTime(2026, 8, 14, 12, 30, 0, DateTimeKind.Utc)
        };

        var decoded = DiscussionReplyCursorCodec.Decode(DiscussionReplyCursorCodec.Encode(reply));

        decoded.Should().Be(new DiscussionReplyCursor(reply.CreatedAt, reply.Id));
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("MQ==")]
    public void Decode_ShouldRejectMalformedValues(string cursor)
    {
        var act = () => DiscussionReplyCursorCodec.Decode(cursor);

        act.Should().Throw<BadRequestException>().WithMessage("The reply cursor is invalid.");
    }
}
