using System.Threading.Channels;

using backend.main.features.clubs.discussions.replies;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs;

public class DiscussionReplyEventBrokerTests
{
    [Fact]
    public void Publish_ShouldDeliverOnlyWithinTheSubscribedClub()
    {
        var broker = new DiscussionReplyEventBroker();
        var first = Channel.CreateUnbounded<DiscussionReplyEvent>();
        var second = Channel.CreateUnbounded<DiscussionReplyEvent>();
        broker.Subscribe(10, Guid.NewGuid(), first.Writer);
        broker.Subscribe(11, Guid.NewGuid(), second.Writer);

        broker.Publish(10, new DiscussionReplyEvent("ReplyCreated", new { id = 1 }));

        first.Reader.TryRead(out var received).Should().BeTrue();
        received!.Type.Should().Be("ReplyCreated");
        second.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public void Unsubscribe_ShouldStopDeliveryAndRemainSafe()
    {
        var broker = new DiscussionReplyEventBroker();
        var channel = Channel.CreateUnbounded<DiscussionReplyEvent>();
        var id = Guid.NewGuid();
        broker.Subscribe(10, id, channel.Writer);
        broker.Unsubscribe(10, id);

        var act = () => broker.Publish(10, new DiscussionReplyEvent("ReplyDeleted", new { id = 1 }));

        act.Should().NotThrow();
        channel.Reader.TryRead(out _).Should().BeFalse();
    }
}
