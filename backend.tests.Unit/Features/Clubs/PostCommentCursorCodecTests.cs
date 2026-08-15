using backend.main.features.clubs.posts.comments;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs;

public class PostCommentCursorCodecTests
{
    [Fact]
    public void EncodeAndDecode_ShouldRoundTripTimestampAndId()
    {
        var comment = new PostComment
        {
            Id = 42,
            CreatedAt = new DateTime(2026, 8, 15, 12, 30, 0, DateTimeKind.Utc)
        };

        var decoded = PostCommentCursorCodec.Decode(PostCommentCursorCodec.Encode(comment));

        decoded.Should().Be(new PostCommentCursor(comment.CreatedAt, comment.Id));
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("MQ==")]
    public void Decode_ShouldRejectMalformedValues(string cursor)
    {
        var act = () => PostCommentCursorCodec.Decode(cursor);

        act.Should().Throw<BadRequestException>().WithMessage("The comment cursor is invalid.");
    }
}
