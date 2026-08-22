using backend.main.features.events;
using backend.main.features.events.images;

using FluentAssertions;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events;

public class EventLifecyclePolicyTests
{
    /// <summary>
    /// Every ordered pair of states, so a move added to the matrix without a deliberate decision
    /// here shows up as a failure rather than slipping through.
    /// </summary>
    public static TheoryData<EventLifecycleState, EventLifecycleState, bool> TransitionMatrix()
    {
        (EventLifecycleState From, EventLifecycleState To)[] allowed =
        [
            (EventLifecycleState.Draft, EventLifecycleState.Published),
            (EventLifecycleState.Published, EventLifecycleState.Paused),
            (EventLifecycleState.Paused, EventLifecycleState.Published),
            (EventLifecycleState.Published, EventLifecycleState.Cancelled),
            (EventLifecycleState.Paused, EventLifecycleState.Cancelled),
            (EventLifecycleState.Cancelled, EventLifecycleState.Published),
            (EventLifecycleState.Published, EventLifecycleState.Archived),
            (EventLifecycleState.Paused, EventLifecycleState.Archived),
            (EventLifecycleState.Cancelled, EventLifecycleState.Archived),
            (EventLifecycleState.Archived, EventLifecycleState.Paused),
        ];

        var data = new TheoryData<EventLifecycleState, EventLifecycleState, bool>();

        foreach (var from in Enum.GetValues<EventLifecycleState>())
        {
            foreach (var to in Enum.GetValues<EventLifecycleState>())
            {
                data.Add(from, to, allowed.Contains((from, to)));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void CanTransition_ShouldAllowOnlyConfiguredLifecycleMoves(
        EventLifecycleState from,
        EventLifecycleState to,
        bool expected)
    {
        EventLifecyclePolicy.CanTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void CanTransition_ShouldKeepDraftOutOfReachOnceAnEventHasBeenLive()
    {
        // Draft means "never been live". Coming back from Published happens only through the
        // time-limited revert, which bypasses this matrix on purpose.
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Published, EventLifecycleState.Draft).Should().BeFalse();
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Paused, EventLifecycleState.Draft).Should().BeFalse();
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Archived, EventLifecycleState.Draft).Should().BeFalse();
    }

    [Fact]
    public void CanTransition_ShouldSendUnarchiveToPausedRatherThanStraightBackToPublished()
    {
        // Recovering an event must not silently re-expose it or reopen registration.
        EventLifecyclePolicy.CanTransition(EventLifecycleState.Archived, EventLifecycleState.Paused).Should().BeTrue();
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
    public void PausedEvents_ShouldLeaveListingsButKeepTheirDetailPageAndStayEditable()
    {
        // Off sale, but people who already registered must not lose the link.
        EventLifecyclePolicy.IsVisibleInPublicListings(EventLifecycleState.Paused).Should().BeFalse();
        EventLifecyclePolicy.IsVisibleInPublicDetail(EventLifecycleState.Paused).Should().BeTrue();
        EventLifecyclePolicy.AllowsRegistration(EventLifecycleState.Paused).Should().BeFalse();

        // Reworking an event while it is off sale is the main reason to pause one.
        EventLifecyclePolicy.AllowsEditing(EventLifecycleState.Paused).Should().BeTrue();
        EventLifecyclePolicy.AllowsEditing(EventLifecycleState.Cancelled).Should().BeFalse();
        EventLifecyclePolicy.AllowsEditing(EventLifecycleState.Archived).Should().BeFalse();
    }

    [Theory]
    [InlineData(EventLifecycleState.Draft, true)]
    [InlineData(EventLifecycleState.Archived, true)]
    [InlineData(EventLifecycleState.Published, false)]
    [InlineData(EventLifecycleState.Paused, false)]
    [InlineData(EventLifecycleState.Cancelled, false)]
    public void AllowsHardDelete_ShouldOnlyCoverStatesWithNoLiveAudience(
        EventLifecycleState state,
        bool expected)
    {
        EventLifecyclePolicy.AllowsHardDelete(state).Should().Be(expected);
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

    [Theory]
    [InlineData(EventLifecycleState.Draft, "publish")]
    [InlineData(EventLifecycleState.Published, "pause,cancel,archive")]
    [InlineData(EventLifecycleState.Paused, "resume,cancel,archive")]
    [InlineData(EventLifecycleState.Cancelled, "reinstate,archive")]
    [InlineData(EventLifecycleState.Archived, "unarchive")]
    public void GetAvailableTransitions_ShouldOfferExactlyTheMovesTheMatrixAllows(
        EventLifecycleState state,
        string expectedKeys)
    {
        var ev = new EventEntity { LifecycleState = state };

        EventLifecyclePolicy.GetAvailableTransitions(ev)
            .Select(transition => transition.Key)
            .Should().Equal(expectedKeys.Split(','));
    }

    [Fact]
    public void GetAvailableTransitions_ShouldOnlyOfferMovesCanTransitionAgreesWith()
    {
        // The client renders buttons straight from this list, so a descriptor the matrix would
        // reject at the service boundary would be a button that always errors.
        foreach (var state in Enum.GetValues<EventLifecycleState>())
        {
            var ev = new EventEntity { LifecycleState = state };

            foreach (var transition in EventLifecyclePolicy.GetAvailableTransitions(ev))
            {
                EventLifecyclePolicy.CanTransition(state, transition.Target)
                    .Should().BeTrue($"'{transition.Key}' is offered from {state}");
            }
        }
    }

    [Fact]
    public void GetAvailableTransitions_ShouldNameTheAudienceOnDestructiveMoves()
    {
        var ev = new EventEntity
        {
            LifecycleState = EventLifecycleState.Published,
            RegistrationCount = 42,
            WaitlistCount = 3
        };

        var cancel = EventLifecyclePolicy.GetAvailableTransitions(ev).Single(t => t.Key == "cancel");

        cancel.IsDestructive.Should().BeTrue();
        cancel.IsReversible.Should().BeTrue("cancelling can now be undone by reinstating");
        cancel.ReversibleNote.Should().NotBeNull();
        cancel.Impacts.Should().Contain(impact =>
            impact.Contains("42 people are registered") && impact.Contains("3 people are on the waitlist"));

        // The whole point of the prompt: pausing must read as the softer option.
        cancel.Impacts.Should().Contain(impact => impact.Contains("pause it instead"));
    }

    [Fact]
    public void GetAvailableTransitions_ShouldNotPadPromptsForAnEventNobodyHasJoined()
    {
        var ev = new EventEntity { LifecycleState = EventLifecycleState.Published };

        EventLifecyclePolicy.GetAvailableTransitions(ev)
            .Single(t => t.Key == "cancel")
            .Impacts.Should().NotContain(impact => impact.Contains("registered"));
    }

    [Fact]
    public void GetAvailableTransitions_ShouldUseSingularWordingForOnePerson()
    {
        var ev = new EventEntity
        {
            LifecycleState = EventLifecycleState.Published,
            RegistrationCount = 1
        };

        EventLifecyclePolicy.GetAvailableTransitions(ev)
            .Single(t => t.Key == "pause")
            .Impacts.Should().Contain(impact => impact.Contains("1 person is registered"));
    }

    [Fact]
    public void GetRevertAvailableUntil_ShouldExpireAfterTheConfiguredWindow()
    {
        var changedAt = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var ev = new EventEntity
        {
            LifecycleState = EventLifecycleState.Cancelled,
            PreviousLifecycleState = EventLifecycleState.Published,
            LifecycleChangedAt = changedAt
        };

        EventLifecyclePolicy.GetRevertAvailableUntil(ev, changedAt.AddHours(23), windowHours: 24)
            .Should().Be(changedAt.AddHours(24));

        EventLifecyclePolicy.GetRevertAvailableUntil(ev, changedAt.AddHours(25), windowHours: 24)
            .Should().BeNull();
    }

    [Fact]
    public void GetRevertAvailableUntil_ShouldBeNullWhenThereIsNothingToUndo()
    {
        var ev = new EventEntity
        {
            LifecycleState = EventLifecycleState.Published,
            PreviousLifecycleState = null,
            LifecycleChangedAt = DateTime.UtcNow
        };

        EventLifecyclePolicy.GetRevertAvailableUntil(ev, DateTime.UtcNow, windowHours: 24).Should().BeNull();
    }
}
