using backend.main.features.events;
using backend.main.features.events.versions.contracts.responses;

using FluentAssertions;

namespace backend.tests.Unit.Features.Events.Versions;

public class EventVersionSnapshotResponseTests
{
    [Fact]
    public void Record_ShouldExposeConstructorValues()
    {
        var response = new EventVersionSnapshotResponse(
            "Event",
            "Desc",
            "Hall",
            true,
            100,
            2500,
            new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc),
            4,
            EventLifecycleState.Published,
            EventCategory.Social,
            "Main Hall",
            "Ottawa",
            45.4,
            -75.7,
            ["campus", "social"]);

        response.Name.Should().Be("Event");
        response.IsPrivate.Should().BeTrue();
        response.Tags.Should().Equal("campus", "social");
        response.LifecycleState.Should().Be(EventLifecycleState.Published);
    }
}

