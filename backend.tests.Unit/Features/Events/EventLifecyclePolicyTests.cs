using backend.main.features.events;
using backend.main.features.events.images;

using FluentAssertions;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events;

public class EventLifecyclePolicyTests
{
    [Fact]
    public void CanTransition_ShouldAllowOnlyConfiguredLifecycleMoves()
    {
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Draft, EventLifecycleState.Published).Should().BeTrue();
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Published, EventLifecycleState.Cancelled).Should().BeTrue();
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Published, EventLifecycleState.Archived).Should().BeTrue();
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Cancelled, EventLifecycleState.Archived).Should().BeTrue();

        EventLifecyclePolicy.CanTransition(EventLifecycleState.Draft, EventLifecycleState.Cancelled).Should().BeFalse();
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Archived, EventLifecycleState.Published).Should().BeFalse();
    }

    [Fact]
    public void VisibilityAndRegistrationRules_ShouldMatchLifecycleState()
    {
        EventLifecyclePolicy.IsVisibleInPublicListings(EventLifecycleState.Published).Should().BeTrue();
        EventLifecyclePolicy.IsVisibleInPublicListings(EventLifecycleState.Cancelled).Should().BeFalse();

        EventLifecyclePolicy.IsVisibleInPublicDetail(EventLifecycleState.Published).Should().BeTrue();
        EventLifecyclePolicy.IsVisibleInPublicDetail(EventLifecycleState.Cancelled).Should().BeTrue();
        EventLifecyclePolicy.IsVisibleInPublicDetail(EventLifecycleState.Draft).Should().BeFalse();

        EventLifecyclePolicy.AllowsRegistration(EventLifecycleState.Published).Should().BeTrue();
        EventLifecyclePolicy.AllowsRegistration(EventLifecycleState.Cancelled).Should().BeFalse();
    }

    [Fact]
    public void AllowsInvitations_ShouldRequirePublishedPrivateEvent()
    {
        EventLifecyclePolicy.AllowsInvitations(new EventEntity
        {
            LifecycleState = EventLifecycleState.Published,
            isPrivate = true
        }).Should().BeTrue();

        EventLifecyclePolicy.AllowsInvitations(new EventEntity
        {
            LifecycleState = EventLifecycleState.Published,
            isPrivate = false
        }).Should().BeFalse();

        EventLifecyclePolicy.AllowsInvitations(new EventEntity
        {
            LifecycleState = EventLifecycleState.Draft,
            isPrivate = true
        }).Should().BeFalse();
    }

    [Fact]
    public void ResolveStatus_ShouldReturnExpectedEventStatus()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        EventLifecyclePolicy.ResolveStatus(new EventEntity(), now).Should().BeNull();
        EventLifecyclePolicy.ResolveStatus(new EventEntity
        {
            StartTime = now.AddHours(1)
        }, now).Should().Be(EventStatus.Upcoming);
        EventLifecyclePolicy.ResolveStatus(new EventEntity
        {
            StartTime = now.AddHours(-1),
            EndTime = now.AddHours(1)
        }, now).Should().Be(EventStatus.Ongoing);
        EventLifecyclePolicy.ResolveStatus(new EventEntity
        {
            StartTime = now.AddHours(-2),
            EndTime = now.AddHours(-1)
        }, now).Should().Be(EventStatus.Closed);
    }

    [Fact]
    public void ResolveStatus_ShouldTreatStartedEventWithoutEndTime_AsOngoing()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);

        EventLifecyclePolicy.ResolveStatus(new EventEntity
        {
            StartTime = now.AddHours(-2),
            EndTime = null
        }, now).Should().Be(EventStatus.Ongoing);
    }

    [Fact]
    public void GetPublishIssues_ShouldReportValidationProblems()
    {
        var ev = new EventEntity
        {
            Name = "x",
            Description = "short",
            Location = new string('a', 51),
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(-2),
            maxParticipants = 0,
            registerCost = 60000,
            isPrivate = true,
            Latitude = 45.0,
            VenueName = new string('v', 101),
            City = new string('c', 101),
            Tags = ["good-tag", "bad tag"],
            Images = []
        };

        var issues = EventLifecyclePolicy.GetPublishIssues(ev, DateTime.UtcNow);

        issues.Should().Contain(message => message.Contains("Name must be between 3 and 30"));
        issues.Should().Contain(message => message.Contains("Description must be between 10 and 200"));
        issues.Should().Contain(message => message.Contains("Location is required"));
        issues.Should().Contain(message => message.Contains("At least one image"));
        issues.Should().Contain(message => message.Contains("Start time must be in the future"));
        issues.Should().Contain(message => message.Contains("End time must be later than start time"));
        issues.Should().Contain(message => message.Contains("Max participants must be between 1 and 10,000"));
        issues.Should().Contain(message => message.Contains("Register cost must be between $0 and $50,000"));
        issues.Should().Contain(message => message.Contains("Private events cannot require a registration fee"));
        issues.Should().Contain(message => message.Contains("Latitude and longitude must both be provided"));
        issues.Should().Contain(message => message.Contains("Venue name must be 100 characters or fewer"));
        issues.Should().Contain(message => message.Contains("City must be 100 characters or fewer"));
        issues.Should().Contain(message => message.Contains("Tag 'bad tag' is invalid"));
    }

    [Fact]
    public void GetPublishIssues_ShouldReportMissingStartTime_TagLimit_AndNegativeCost()
    {
        var now = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc);
        var ev = new EventEntity
        {
            Name = "Valid Name",
            Description = "This description is comfortably valid.",
            Location = "Student Center",
            StartTime = null,
            maxParticipants = 100,
            registerCost = -1,
            Tags =
            [
                "one", "two", "three", "four", "five",
                "six", "seven", "eight", "nine", "ten", "eleven", ""
            ],
            Images = [new EventImage { ImageUrl = "https://cdn.test/event.png" }]
        };

        var issues = EventLifecyclePolicy.GetPublishIssues(ev, now);

        issues.Should().Contain("Start time is required.");
        issues.Should().Contain("Register cost must be between $0 and $50,000.");
        issues.Should().Contain("A maximum of 10 tags are allowed.");
        issues.Should().Contain(message => message.Contains("Tag '' is invalid"));
    }

    [Fact]
    public void GetPublishIssues_ShouldAcceptValidEvent()
    {
        var ev = new EventEntity
        {
            Name = "Campus Mixer",
            Description = "A welcoming social mixer for new and returning students.",
            Location = "Student Center",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(3),
            maxParticipants = 100,
            registerCost = 0,
            isPrivate = false,
            Latitude = 45.4215,
            Longitude = -75.6972,
            VenueName = "Main Hall",
            City = "Ottawa",
            Tags = ["social", "campus"],
            Images = [new EventImage { ImageUrl = "https://cdn.test/events/1.png" }]
        };

        EventLifecyclePolicy.GetPublishIssues(ev, DateTime.UtcNow).Should().BeEmpty();
    }
}
