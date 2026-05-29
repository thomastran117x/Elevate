using System.Threading.Channels;

using backend.main.features.clubs.posts.comments;

using FluentAssertions;

namespace backend.tests.Unit.Features.Clubs;

public class CommentEventBrokerTests
{
    [Fact]
    public void Publish_ShouldDeliverEventToSubscriber()
    {
        var broker = new CommentEventBroker();
        var channel = Channel.CreateUnbounded<CommentEvent>();
        broker.Subscribe(1, Guid.NewGuid(), channel.Writer);

        broker.Publish(1, new CommentEvent("CommentCreated", new { id = 10 }));

        channel.Reader.TryRead(out var received).Should().BeTrue();
        received!.Type.Should().Be("CommentCreated");
    }

    [Fact]
    public void Publish_ShouldDeliverToAllSubscribersForSamePost()
    {
        var broker = new CommentEventBroker();
        var channelA = Channel.CreateUnbounded<CommentEvent>();
        var channelB = Channel.CreateUnbounded<CommentEvent>();
        broker.Subscribe(1, Guid.NewGuid(), channelA.Writer);
        broker.Subscribe(1, Guid.NewGuid(), channelB.Writer);

        broker.Publish(1, new CommentEvent("CommentUpdated", new { id = 5 }));

        channelA.Reader.TryRead(out var a).Should().BeTrue();
        channelB.Reader.TryRead(out var b).Should().BeTrue();
        a!.Type.Should().Be("CommentUpdated");
        b!.Type.Should().Be("CommentUpdated");
    }

    [Fact]
    public void Publish_ShouldNotDeliverToSubscribersForDifferentPost()
    {
        var broker = new CommentEventBroker();
        var channel = Channel.CreateUnbounded<CommentEvent>();
        broker.Subscribe(postId: 2, Guid.NewGuid(), channel.Writer);

        broker.Publish(postId: 1, new CommentEvent("CommentDeleted", new { id = 3 }));

        channel.Reader.TryRead(out _).Should().BeFalse("event was published to post 1 but subscriber is on post 2");
    }

    [Fact]
    public void Publish_WithNoSubscribers_IsANoOp()
    {
        var broker = new CommentEventBroker();

        var act = () => broker.Publish(99, new CommentEvent("CommentCreated", new { }));

        act.Should().NotThrow();
    }

    [Fact]
    public void Unsubscribe_ShouldStopEventDelivery()
    {
        var broker = new CommentEventBroker();
        var channel = Channel.CreateUnbounded<CommentEvent>();
        var id = Guid.NewGuid();
        broker.Subscribe(1, id, channel.Writer);
        broker.Unsubscribe(1, id);

        broker.Publish(1, new CommentEvent("CommentCreated", new { id = 7 }));

        channel.Reader.TryRead(out _).Should().BeFalse("subscriber was removed before publish");
    }

    [Fact]
    public void Unsubscribe_LastSubscriber_CleansUpPostEntry()
    {
        var broker = new CommentEventBroker();
        var channel = Channel.CreateUnbounded<CommentEvent>();
        var id = Guid.NewGuid();
        broker.Subscribe(1, id, channel.Writer);
        broker.Unsubscribe(1, id);

        // Publishing after all subscribers are gone should be a safe no-op
        var act = () => broker.Publish(1, new CommentEvent("CommentCreated", new { }));
        act.Should().NotThrow();
    }

    [Fact]
    public void Subscribe_MultipleIds_EachReceivesOwnEvents()
    {
        var broker = new CommentEventBroker();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var channelA = Channel.CreateUnbounded<CommentEvent>();
        var channelB = Channel.CreateUnbounded<CommentEvent>();

        broker.Subscribe(1, idA, channelA.Writer);
        broker.Subscribe(1, idB, channelB.Writer);
        broker.Unsubscribe(1, idA); // remove A

        broker.Publish(1, new CommentEvent("CommentCreated", new { }));

        channelA.Reader.TryRead(out _).Should().BeFalse("A was unsubscribed");
        channelB.Reader.TryRead(out _).Should().BeTrue("B is still subscribed");
    }
}
