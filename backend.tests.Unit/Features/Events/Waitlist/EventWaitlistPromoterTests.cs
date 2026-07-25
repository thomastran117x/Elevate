using System.Data;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.access;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.waitlist;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Unit.Features.Events.Waitlist;

public class EventWaitlistPromoterTests
{
    [Fact]
    public async Task PromoteAsync_ShouldPromoteFirstInQueue_CreatingActiveRegistration_CopyingDetails()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.FillEventAsync(capacity: 1, occupantUserId: 4);
        await harness.QueueAsync(userId: 2, notes: "Vegetarian please", phone: "555-0101", diet: "Nuts");
        await harness.QueueAsync(userId: 3);

        // Free a seat, then promote.
        await harness.CancelRegistrationAsync(userId: 4);

        var promoted = await harness.PromoteAsync();

        promoted.Should().HaveCount(1);
        promoted[0].UserId.Should().Be(2);

        var registration = await harness.Db.EventRegistrations
            .SingleAsync(r => r.UserId == 2 && r.EventId == harness.EventId);
        registration.Status.Should().Be(RegistrationStatus.Active);
        registration.Notes.Should().Be("Vegetarian please");
        registration.PhoneNumber.Should().Be("555-0101");
        registration.DietaryNeeds.Should().Be("Nuts");

        var entry = await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 2);
        entry.Status.Should().Be(EventWaitlistEntryStatus.Promoted);
        entry.PromotedAtUtc.Should().NotBeNull();
        entry.PromotionEmailQueuedAtUtc.Should().NotBeNull();

        // Second in line stays queued — only one seat was free.
        var stillWaiting = await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 3);
        stillWaiting.Status.Should().Be(EventWaitlistEntryStatus.Waiting);
    }

    [Fact]
    public async Task PromoteAsync_ShouldReturnEmpty_WhenNoFreeSeats()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.FillEventAsync(capacity: 1, occupantUserId: 4);
        await harness.QueueAsync(userId: 2);

        var promoted = await harness.PromoteAsync();

        promoted.Should().BeEmpty();
        (await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 2))
            .Status.Should().Be(EventWaitlistEntryStatus.Waiting);
    }

    [Fact]
    public async Task PromoteAsync_ShouldReturnEmpty_WhenWaitlistDisabled()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync(waitlistEnabled: false);
        await harness.SetCapacityAsync(5);
        await harness.QueueAsync(userId: 2);

        (await harness.PromoteAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(EventLifecycleState.Draft)]
    [InlineData(EventLifecycleState.Cancelled)]
    [InlineData(EventLifecycleState.Archived)]
    public async Task PromoteAsync_ShouldReturnEmpty_WhenNotPublished(EventLifecycleState state)
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(5);
        await harness.QueueAsync(userId: 2);
        await harness.SetLifecycleStateAsync(state);

        (await harness.PromoteAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PromoteAsync_ShouldReturnEmpty_WhenEventIsPaid()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(5);
        await harness.QueueAsync(userId: 2);
        await harness.SetRegisterCostAsync(2500);

        (await harness.PromoteAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PromoteAsync_ShouldReturnEmpty_WhenEventAlreadyStarted()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(5);
        await harness.QueueAsync(userId: 2);
        await harness.SetStartTimeAsync(DateTime.UtcNow.AddHours(-1));

        (await harness.PromoteAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PromoteAsync_ShouldSkipDisabledUser_AndPromoteNext_LeavingSkippedEntryWaiting()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(1);
        await harness.QueueAsync(userId: 2);
        await harness.QueueAsync(userId: 3);
        await harness.DisableUserAsync(2);

        var promoted = await harness.PromoteAsync();

        promoted.Should().HaveCount(1);
        promoted[0].UserId.Should().Be(3);

        // Skipped in place — reversible if the account is re-enabled.
        (await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 2))
            .Status.Should().Be(EventWaitlistEntryStatus.Waiting);
    }

    [Fact]
    public async Task PromoteAsync_ShouldSkipUserWhoLostVisibility_OnPrivateEvent()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(1);
        await harness.QueueAsync(userId: 2);
        await harness.QueueAsync(userId: 3);

        // User 2's invitation was revoked after they joined the queue.
        harness.DenyAccessFor(userId: 2);

        var promoted = await harness.PromoteAsync();

        promoted.Should().HaveCount(1);
        promoted[0].UserId.Should().Be(3);

        (await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 2))
            .Status.Should().Be(EventWaitlistEntryStatus.Waiting);

        (await harness.Db.EventRegistrations.AnyAsync(r => r.UserId == 2))
            .Should().BeFalse("a user who lost access must never be auto-registered");
    }

    [Fact]
    public async Task PromoteAsync_ShouldCloseEntryWithoutConsumingSeat_WhenUserRegisteredDirectly()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(2);
        await harness.QueueAsync(userId: 2);
        await harness.QueueAsync(userId: 3);

        // User 2 grabbed a seat directly while queued; one seat remains.
        await harness.RegisterAsync(userId: 2);

        var promoted = await harness.PromoteAsync();

        // The freed seat must go to user 3, not be burned on user 2's no-op.
        promoted.Should().HaveCount(1);
        promoted[0].UserId.Should().Be(3);

        var closedEntry = await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 2);
        closedEntry.Status.Should().Be(EventWaitlistEntryStatus.Promoted);
        closedEntry.PromotionEmailQueuedAtUtc.Should().BeNull("no email is owed — they registered themselves");
    }

    [Fact]
    public async Task PromoteAsync_ShouldReactivateCancelledRegistrationRow_NotInsertDuplicate()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(1);

        // User 2 registered, cancelled, then rejoined the waitlist. The unique
        // (EventId, UserId) index means promotion MUST reactivate, not insert.
        await harness.RegisterAsync(userId: 2);
        await harness.CancelRegistrationAsync(userId: 2);
        await harness.QueueAsync(userId: 2);

        var promoted = await harness.PromoteAsync();

        promoted.Should().HaveCount(1);
        var registrations = await harness.Db.EventRegistrations
            .Where(r => r.UserId == 2 && r.EventId == harness.EventId)
            .ToListAsync();
        registrations.Should().HaveCount(1);
        registrations[0].Status.Should().Be(RegistrationStatus.Active);
        registrations[0].CancelledAt.Should().BeNull();
    }

    [Fact]
    public async Task PromoteAsync_ShouldPromoteN_WhenNSeatsFreed()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(3);
        await harness.QueueAsync(userId: 2);
        await harness.QueueAsync(userId: 3);
        await harness.QueueAsync(userId: 4);
        await harness.QueueAsync(userId: 5);

        var promoted = await harness.PromoteAsync();

        promoted.Should().HaveCount(3);
        promoted.Select(p => p.UserId).Should().ContainInOrder(2, 3, 4);

        (await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 5))
            .Status.Should().Be(EventWaitlistEntryStatus.Waiting);
    }

    [Fact]
    public async Task PromoteAsync_ShouldPromoteInJoinOrder_TieBreakingOnId()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(2);

        // Identical JoinedAtUtc — ordering must fall back to Id for a total order.
        var sharedInstant = DateTime.UtcNow.AddMinutes(-5);
        await harness.QueueAsync(userId: 4, joinedAtUtc: sharedInstant);
        await harness.QueueAsync(userId: 3, joinedAtUtc: sharedInstant);
        await harness.QueueAsync(userId: 2, joinedAtUtc: sharedInstant);

        var promoted = await harness.PromoteAsync();

        promoted.Select(p => p.UserId).Should().ContainInOrder(4, 3);
    }

    [Fact]
    public async Task PromoteAsync_ShouldDrainQueue_WhenCapacityIsUnlimited()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.QueueAsync(userId: 2);
        await harness.QueueAsync(userId: 3);
        // Capacity lowered to unlimited can strand an existing queue.
        await harness.SetCapacityAsync(0);

        var promoted = await harness.PromoteAsync();

        promoted.Should().HaveCount(2);
    }

    [Fact]
    public async Task PromoteStandaloneAsync_ShouldRecomputeCountersAndPublishEmails()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(2);
        await harness.QueueAsync(userId: 2);
        await harness.QueueAsync(userId: 3);

        var count = await harness.Promoter.PromoteStandaloneAsync(harness.EventId);

        count.Should().Be(2);

        var ev = await harness.Db.Events.AsNoTracking().SingleAsync(e => e.Id == harness.EventId);
        ev.RegistrationCount.Should().Be(2);
        ev.WaitlistCount.Should().Be(0);

        harness.PublishedEmails.Should().HaveCount(2);
        harness.PublishedEmails.Should().AllSatisfy(message =>
        {
            message.Type.Should().Be(EmailMessageType.WaitlistPromoted);
            message.EventId.Should().Be(harness.EventId);
        });
    }

    [Fact]
    public async Task PromoteStandaloneAsync_ShouldReturnZero_AndNotThrow_WhenLockUnavailable()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        await harness.SetCapacityAsync(2);
        await harness.QueueAsync(userId: 2);
        harness.MakeLockUnavailable();

        var count = await harness.Promoter.PromoteStandaloneAsync(harness.EventId);

        count.Should().Be(0);
        (await harness.Db.EventWaitlistEntries.SingleAsync(w => w.UserId == 2))
            .Status.Should().Be(EventWaitlistEntryStatus.Waiting);
    }

    [Fact]
    public async Task PublishPromotionEmailsAsync_ShouldNotThrow_WhenPublisherFails()
    {
        await using var harness = await WaitlistPromoterHarness.CreateAsync();
        harness.MakePublisherThrow();

        var promotions = new List<WaitlistPromotion> { new(1, 2, "a@test.local", "A") };

        // The promotion is already durable — a broken publisher must not surface as an error.
        var act = async () => await harness.Promoter.PublishPromotionEmailsAsync(
            promotions, harness.EventId, "Event", DateTime.UtcNow.AddDays(1));

        await act.Should().NotThrowAsync();
    }
}

internal sealed class WaitlistPromoterHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Mock<IEventAccessChecker> _accessCheckerMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly HashSet<int> _deniedUserIds = [];

    public AppDatabaseContext Db { get; }
    public EventWaitlistPromoter Promoter { get; }
    public List<EmailMessage> PublishedEmails { get; } = [];
    public int EventId => 1;

    private WaitlistPromoterHarness(
        SqliteConnection connection,
        AppDatabaseContext db,
        EventWaitlistPromoter promoter,
        Mock<IEventAccessChecker> accessCheckerMock,
        Mock<ICacheService> cacheMock,
        Mock<IPublisher> publisherMock)
    {
        _connection = connection;
        Db = db;
        Promoter = promoter;
        _accessCheckerMock = accessCheckerMock;
        _cacheMock = cacheMock;
        _publisherMock = publisherMock;
    }

    public static async Task<WaitlistPromoterHarness> CreateAsync(bool waitlistEnabled = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDatabaseContext(options);
        await db.Database.EnsureCreatedAsync();

        db.Users.AddRange(
            new User { Id = 1, Email = "organizer@test.local", Usertype = "Organizer" },
            new User { Id = 2, Email = "one@test.local", Name = "One", Usertype = "Participant" },
            new User { Id = 3, Email = "two@test.local", Name = "Two", Usertype = "Participant" },
            new User { Id = 4, Email = "three@test.local", Name = "Three", Usertype = "Participant" },
            new User { Id = 5, Email = "four@test.local", Name = "Four", Usertype = "Participant" });

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
            Description = "A published event used for waitlist promotion tests.",
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

        var harnessRef = new WaitlistPromoterHarness[1];

        var accessCheckerMock = new Mock<IEventAccessChecker>();
        accessCheckerMock
            .Setup(checker => checker.CanViewEventAsync(
                It.IsAny<backend.main.features.events.Events>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync((backend.main.features.events.Events _, int? userId, string? _) =>
                harnessRef[0] == null || userId == null || !harnessRef[0]._deniedUserIds.Contains(userId.Value));

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(cache => cache.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);
        cacheMock.Setup(cache => cache.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        cacheMock.Setup(cache => cache.DeleteKeyAsync(It.IsAny<string>())).ReturnsAsync(true);
        cacheMock.Setup(cache => cache.SetMembersAsync(It.IsAny<string>())).ReturnsAsync([]);

        var refreshCacheMock = new Mock<IRefreshAheadCache>();
        refreshCacheMock.Setup(cache => cache.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var publisherMock = new Mock<IPublisher>();

        var promoter = new EventWaitlistPromoter(
            db,
            accessCheckerMock.Object,
            cacheMock.Object,
            refreshCacheMock.Object,
            Mock.Of<IEventSearchOutboxWriter>(),
            publisherMock.Object);

        var harness = new WaitlistPromoterHarness(
            connection, db, promoter, accessCheckerMock, cacheMock, publisherMock);
        harnessRef[0] = harness;

        publisherMock
            .Setup(publisher => publisher.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
            .Callback<string, EmailMessage>((_, message) => harness.PublishedEmails.Add(message))
            .Returns(Task.CompletedTask);

        return harness;
    }

    /// <summary>
    /// Runs promotion the way a caller must: inside a Serializable transaction it owns.
    /// </summary>
    public async Task<IReadOnlyList<WaitlistPromotion>> PromoteAsync()
    {
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var promoted = await Promoter.PromoteWithinTransactionAsync(EventId, DateTime.UtcNow);
        await transaction.CommitAsync();
        return promoted;
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

    public async Task SetLifecycleStateAsync(EventLifecycleState state)
    {
        var ev = await Db.Events.SingleAsync(e => e.Id == EventId);
        ev.LifecycleState = state;
        await Db.SaveChangesAsync();
    }

    public async Task SetStartTimeAsync(DateTime startTime)
    {
        var ev = await Db.Events.SingleAsync(e => e.Id == EventId);
        ev.StartTime = startTime;
        await Db.SaveChangesAsync();
    }

    public async Task FillEventAsync(int capacity, int occupantUserId)
    {
        await SetCapacityAsync(capacity);
        await RegisterAsync(occupantUserId);
    }

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

    public async Task CancelRegistrationAsync(int userId)
    {
        var registration = await Db.EventRegistrations
            .SingleAsync(r => r.EventId == EventId && r.UserId == userId);
        registration.Status = RegistrationStatus.Cancelled;
        registration.CancelledAt = DateTime.UtcNow;
        await Db.SaveChangesAsync();
    }

    public async Task QueueAsync(
        int userId,
        string? notes = null,
        string? phone = null,
        string? diet = null,
        DateTime? joinedAtUtc = null)
    {
        // Stagger joins so queue order is deterministic unless a caller pins the instant.
        var joined = joinedAtUtc ?? DateTime.UtcNow.AddMinutes(-100 + userId);

        Db.EventWaitlistEntries.Add(new EventWaitlistEntry
        {
            EventId = EventId,
            UserId = userId,
            Status = EventWaitlistEntryStatus.Waiting,
            JoinedAtUtc = joined,
            Notes = notes,
            PhoneNumber = phone,
            DietaryNeeds = diet,
            CreatedAt = joined,
            UpdatedAt = joined
        });
        await Db.SaveChangesAsync();
    }

    public async Task DisableUserAsync(int userId)
    {
        var user = await Db.Users.SingleAsync(u => u.Id == userId);
        user.IsDisabled = true;
        await Db.SaveChangesAsync();
    }

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
