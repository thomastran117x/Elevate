using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.access;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.waitlist;
using backend.main.features.events.waitlist.contracts.requests;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Unit.Features.Events.Waitlist;

public class EventWaitlistServiceTests
{
    [Fact]
    public async Task JoinAsync_ShouldCreateEntry_WhenEventIsFull()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();

        var entry = await harness.Service.JoinAsync(
            harness.EventId,
            harness.UserId,
            "Participant",
            new JoinWaitlistRequest { Notes = "  keen  ", PhoneNumber = " 555 ", DietaryNeeds = "  " });

        entry.Position.Should().Be(1);
        entry.Status.Should().Be(nameof(EventWaitlistEntryStatus.Waiting));

        var stored = await harness.Db.EventWaitlistEntries.SingleAsync();
        stored.Notes.Should().Be("keen");
        stored.PhoneNumber.Should().Be("555");
        stored.DietaryNeeds.Should().BeNull("whitespace-only input is sanitized to null");

        (await harness.Db.Events.SingleAsync()).WaitlistCount.Should().Be(1);
    }

    [Fact]
    public async Task JoinAsync_ShouldPublishWaitlistJoinedEmail()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();

        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        var email = harness.PublishedEmails.Should().ContainSingle().Subject;
        email.Type.Should().Be(EmailMessageType.WaitlistJoined);
        email.EventId.Should().Be(harness.EventId);
    }

    [Fact]
    public async Task JoinAsync_ShouldSucceed_WhenPublisherThrows()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        harness.MakePublisherThrow();

        var entry = await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        entry.Position.Should().Be(1);
        (await harness.Db.EventWaitlistEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowConflict_WhenSeatsAreStillAvailable()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("Seats are still available — register instead");
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowConflict_WhenAlreadyRegistered()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.SetCapacityAsync(2);
        await harness.RegisterAsync(harness.UserId);
        await harness.RegisterAsync(harness.OtherUserId);

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("You're already registered for this event");
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowConflict_WhenAlreadyWaiting()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("You're already on the waitlist for this event");
    }

    [Fact]
    public async Task JoinAsync_ShouldReactivateLeftEntry_AndMoveItToTheBackOfTheQueue()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();

        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");
        var originalJoinedAt = (await harness.Db.EventWaitlistEntries.AsNoTracking().SingleAsync()).JoinedAtUtc;

        await harness.Service.LeaveAsync(harness.EventId, harness.UserId, "Participant");
        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        // Reactivated in place — the unique (EventId, UserId) index forbids a second row.
        var entries = await harness.Db.EventWaitlistEntries.AsNoTracking().ToListAsync();
        entries.Should().ContainSingle();
        entries[0].Status.Should().Be(EventWaitlistEntryStatus.Waiting);
        entries[0].LeftAtUtc.Should().BeNull();
        entries[0].JoinedAtUtc.Should().BeOnOrAfter(originalJoinedAt);
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowBadRequest_WhenWaitlistDisabled()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync(waitlistEnabled: false);
        await harness.FillEventAsync();

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<BadRequestException>())
            .WithMessage("This event does not have a waitlist.");
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowBadRequest_WhenEventIsPaid()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.SetRegisterCostAsync(1500);

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<BadRequestException>())
            .WithMessage("Waitlists are not available for paid events.");
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowBadRequest_WhenCapacityIsUnlimited()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.SetCapacityAsync(0);

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<BadRequestException>())
            .WithMessage("This event has unlimited capacity — you can register directly.");
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowConflict_WhenEventAlreadyStarted()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.SetStartTimeAsync(DateTime.UtcNow.AddHours(-1));

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowConflict_WhenEventIsCancelled()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.SetLifecycleStateAsync(EventLifecycleState.Cancelled);

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("The waitlist is only available for published events.");
    }

    [Fact]
    public async Task JoinAsync_ShouldThrowConflict_WhenLockUnavailable()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        harness.MakeLockUnavailable();

        var act = async () => await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task LeaveAsync_ShouldMarkLeft_AndDecrementWaitlistCount()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        await harness.Service.LeaveAsync(harness.EventId, harness.UserId, "Participant");

        var entry = await harness.Db.EventWaitlistEntries.AsNoTracking().SingleAsync();
        entry.Status.Should().Be(EventWaitlistEntryStatus.Left);
        entry.LeftAtUtc.Should().NotBeNull();
        (await harness.Db.Events.AsNoTracking().SingleAsync()).WaitlistCount.Should().Be(0);
    }

    [Fact]
    public async Task LeaveAsync_ShouldThrowNotFound_WhenNotWaiting()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();

        var act = async () => await harness.Service.LeaveAsync(harness.EventId, harness.UserId, "Participant");

        (await act.Should().ThrowAsync<ResourceNotFoundException>())
            .WithMessage("You're not on the waitlist for this event");
    }

    [Fact]
    public async Task GetMyStatusAsync_ShouldComputePosition_AndResequenceWhenSomeoneLeaves()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();

        await harness.Service.JoinAsync(harness.EventId, 3, "Participant");
        await harness.Service.JoinAsync(harness.EventId, 4, "Participant");
        await harness.Service.JoinAsync(harness.EventId, 5, "Participant");

        (await harness.Service.GetMyStatusAsync(harness.EventId, 5, "Participant"))
            .Position.Should().Be(3);

        // The middle of the queue leaves — everyone below shifts up automatically because
        // position is computed, not stored.
        await harness.Service.LeaveAsync(harness.EventId, 4, "Participant");

        var status = await harness.Service.GetMyStatusAsync(harness.EventId, 5, "Participant");
        status.OnWaitlist.Should().BeTrue();
        status.Position.Should().Be(2);
        status.WaitlistCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMyStatusAsync_ShouldReportNotOnWaitlist_ForANonMember()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, 3, "Participant");

        var status = await harness.Service.GetMyStatusAsync(harness.EventId, 5, "Participant");

        status.OnWaitlist.Should().BeFalse();
        status.Position.Should().BeNull();
        status.WaitlistCount.Should().Be(1);
    }

    [Fact]
    public async Task GetEventWaitlistAsync_ShouldReturnOrderedRosterWithPii()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, 3, "Participant",
            new JoinWaitlistRequest { PhoneNumber = "555-0102" });
        await harness.Service.JoinAsync(harness.EventId, 4, "Participant");

        var (entries, totalCount) = await harness.Service.GetEventWaitlistAsync(
            harness.EventId, harness.OrganizerUserId, "Organizer");

        totalCount.Should().Be(2);
        entries.Select(e => e.Position).Should().ContainInOrder(1, 2);
        entries[0].UserId.Should().Be(3);
        entries[0].UserEmail.Should().Be("three@test.local");
        entries[0].PhoneNumber.Should().Be("555-0102");
    }

    [Fact]
    public async Task RemoveEntryAsync_ShouldMarkRemoved_AndRecordTheActor()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        var entry = await harness.Service.JoinAsync(harness.EventId, 3, "Participant");

        await harness.Service.RemoveEntryAsync(
            harness.EventId, entry.Id, harness.OrganizerUserId, "Organizer");

        var stored = await harness.Db.EventWaitlistEntries.AsNoTracking().SingleAsync();
        stored.Status.Should().Be(EventWaitlistEntryStatus.Removed);
        stored.RemovedByUserId.Should().Be(harness.OrganizerUserId);
        (await harness.Db.Events.AsNoTracking().SingleAsync()).WaitlistCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMyWaitlistsAsync_ShouldReturnOnlyWaitingEntries()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, 3, "Participant");

        var mine = await harness.Service.GetMyWaitlistsAsync(3, "Participant");
        mine.Should().ContainSingle();
        mine[0].Position.Should().Be(1);
        mine[0].Event.Id.Should().Be(harness.EventId);

        await harness.Service.LeaveAsync(harness.EventId, 3, "Participant");

        (await harness.Service.GetMyWaitlistsAsync(3, "Participant")).Should().BeEmpty();
    }

    [Fact]
    public async Task LeaveAsync_ShouldSucceed_EvenAfterEventAccessIsRevoked()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        // The promoter deliberately leaves such entries Waiting, so the user must still be able
        // to withdraw — otherwise their contact details stay stored until an organizer intervenes.
        harness.RevokeEventVisibility();

        await harness.Service.LeaveAsync(harness.EventId, harness.UserId, "Participant");

        (await harness.Db.EventWaitlistEntries.AsNoTracking().SingleAsync())
            .Status.Should().Be(EventWaitlistEntryStatus.Left);
    }

    [Fact]
    public async Task GetMyWaitlistsAsync_ShouldOmitEventsTheUserCanNoLongerSee()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, harness.UserId, "Participant");

        (await harness.Service.GetMyWaitlistsAsync(harness.UserId, "Participant")).Should().ContainSingle();

        // A revoked private-event invitation must close the window onto the event details.
        harness.DenyAccessFor(harness.UserId);

        (await harness.Service.GetMyWaitlistsAsync(harness.UserId, "Participant")).Should().BeEmpty();

        // ...but the entry survives, so they can still leave the queue.
        (await harness.Db.EventWaitlistEntries.AsNoTracking().SingleAsync())
            .Status.Should().Be(EventWaitlistEntryStatus.Waiting);
    }

    [Fact]
    public async Task PromoteNextAsync_ShouldThrowConflict_WhenNoSeatsAvailable()
    {
        await using var harness = await WaitlistServiceHarness.CreateAsync();
        await harness.FillEventAsync();
        await harness.Service.JoinAsync(harness.EventId, 3, "Participant");

        var act = async () => await harness.Service.PromoteNextAsync(
            harness.EventId, harness.OrganizerUserId, "Organizer");

        (await act.Should().ThrowAsync<ConflictException>())
            .WithMessage("No seats are available to promote into.");
    }
}

internal sealed class WaitlistServiceHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<IEventsService> _eventsServiceMock;
    private readonly HashSet<int> _deniedUserIds = [];

    public AppDatabaseContext Db { get; }
    public EventWaitlistService Service { get; }
    public List<EmailMessage> PublishedEmails { get; } = [];
    public int EventId => 1;
    public int OrganizerUserId => 1;
    public int UserId => 2;
    public int OtherUserId => 3;

    private WaitlistServiceHarness(
        SqliteConnection connection,
        AppDatabaseContext db,
        EventWaitlistService service,
        Mock<ICacheService> cacheMock,
        Mock<IPublisher> publisherMock,
        Mock<IEventsService> eventsServiceMock)
    {
        _connection = connection;
        Db = db;
        Service = service;
        _cacheMock = cacheMock;
        _publisherMock = publisherMock;
        _eventsServiceMock = eventsServiceMock;
    }

    public static async Task<WaitlistServiceHarness> CreateAsync(bool waitlistEnabled = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDatabaseContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.AddRange(
            new User { Id = 1, Email = "organizer@test.local", Name = "Organizer", Usertype = "Organizer" },
            new User { Id = 2, Email = "two@test.local", Name = "Two", Usertype = "Participant" },
            new User { Id = 3, Email = "three@test.local", Name = "Three", Usertype = "Participant" },
            new User { Id = 4, Email = "four@test.local", Name = "Four", Usertype = "Participant" },
            new User { Id = 5, Email = "five@test.local", Name = "Five", Usertype = "Participant" });

        db.Clubs.Add(new Club
        {
            Id = 1,
            UserId = 1,
            Name = "Waitlist Club",
            Description = "Waitlist coverage club",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/waitlist.png"
        });

        db.Events.Add(new backend.main.features.events.Events
        {
            Id = 1,
            ClubId = 1,
            Name = "Waitlisted Event",
            Description = "A published event used for waitlist service tests.",
            Location = "Student Center",
            LifecycleState = EventLifecycleState.Published,
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            maxParticipants = 1,
            registerCost = 0,
            WaitlistEnabled = waitlistEnabled,
            Category = EventCategory.Other
        });

        await db.SaveChangesAsync();

        var eventsServiceMock = new Mock<IEventsService>();
        eventsServiceMock
            .Setup(service => service.EnsureCanViewEventAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        eventsServiceMock
            .Setup(service => service.GetManageableEvent(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(() => db.Events.AsNoTracking().Single(e => e.Id == 1));

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(cache => cache.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);
        cacheMock.Setup(cache => cache.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        cacheMock.Setup(cache => cache.DeleteKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
        cacheMock.Setup(cache => cache.SetMembersAsync(It.IsAny<string>())).ReturnsAsync([]);

        var refreshCacheMock = new Mock<IRefreshAheadCache>();
        refreshCacheMock.Setup(cache => cache.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var harnessRef = new WaitlistServiceHarness[1];

        var accessCheckerMock = new Mock<IEventAccessChecker>();
        accessCheckerMock
            .Setup(checker => checker.CanViewEventAsync(
                It.IsAny<backend.main.features.events.Events>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync((backend.main.features.events.Events _, int? userId, string? _) =>
                harnessRef[0] == null || userId == null || !harnessRef[0]._deniedUserIds.Contains(userId.Value));

        var publisherMock = new Mock<IPublisher>();
        var outboxWriter = Mock.Of<IEventSearchOutboxWriter>();

        var promoter = new EventWaitlistPromoter(
            db, accessCheckerMock.Object, cacheMock.Object, refreshCacheMock.Object, outboxWriter, publisherMock.Object);

        var service = new EventWaitlistService(
            db,
            new EventWaitlistRepository(db),
            promoter,
            eventsServiceMock.Object,
            accessCheckerMock.Object,
            cacheMock.Object,
            refreshCacheMock.Object,
            outboxWriter,
            publisherMock.Object);

        var harness = new WaitlistServiceHarness(connection, db, service, cacheMock, publisherMock, eventsServiceMock);
        harnessRef[0] = harness;

        publisherMock
            .Setup(publisher => publisher.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
            .Callback<string, EmailMessage>((_, message) => harness.PublishedEmails.Add(message))
            .Returns(Task.CompletedTask);

        return harness;
    }

    /// <summary>Fills the single seat so the waitlist becomes joinable.</summary>
    public Task FillEventAsync() => RegisterAsync(OrganizerUserId);

    public async Task RegisterAsync(int userId)
    {
        Db.EventRegistrations.Add(new EventRegistration
        {
            EventId = EventId,
            UserId = userId,
            Status = RegistrationStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();
    }

    public async Task SetCapacityAsync(int capacity)
    {
        var ev = await Db.Events.SingleAsync(e => e.Id == EventId);
        ev.maxParticipants = capacity;
        await Db.SaveChangesAsync();
    }

    public async Task SetRegisterCostAsync(int cost)
    {
        var ev = await Db.Events.SingleAsync(e => e.Id == EventId);
        ev.registerCost = cost;
        await Db.SaveChangesAsync();
    }

    public async Task SetStartTimeAsync(DateTime startTime)
    {
        var ev = await Db.Events.SingleAsync(e => e.Id == EventId);
        ev.StartTime = startTime;
        await Db.SaveChangesAsync();
    }

    public async Task SetLifecycleStateAsync(EventLifecycleState state)
    {
        var ev = await Db.Events.SingleAsync(e => e.Id == EventId);
        ev.LifecycleState = state;
        await Db.SaveChangesAsync();
    }

    /// <summary>Makes the events service reject reads of this event, as a revocation would.</summary>
    public void RevokeEventVisibility() =>
        _eventsServiceMock
            .Setup(service => service.EnsureCanViewEventAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new ResourceNotFoundException("Event not found"));

    public void DenyAccessFor(int userId) => _deniedUserIds.Add(userId);

    public void MakeLockUnavailable() =>
        _cacheMock
            .Setup(cache => cache.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

    public void MakePublisherThrow() =>
        _publisherMock
            .Setup(publisher => publisher.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
            .ThrowsAsync(new InvalidOperationException("kafka is down"));

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
