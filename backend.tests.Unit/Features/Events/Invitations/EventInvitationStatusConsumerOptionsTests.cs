using backend.main.features.events.invitations;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Invitations;

public class EventInvitationStatusConsumerOptionsTests
{
    [Fact]
    public void Constructor_ShouldAssignValues()
    {
        var options = new EventInvitationStatusConsumerOptions("kafka:9092", "topic", "group");

        options.BootstrapServers.Should().Be("kafka:9092");
        options.Topic.Should().Be("topic");
        options.GroupId.Should().Be("group");
    }
}

