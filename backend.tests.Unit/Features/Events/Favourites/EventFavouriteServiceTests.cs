using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.access;
using backend.main.features.events.favourites;
using backend.main.features.events.registration;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Unit.Features.Events.Favourites;

public class EventFavouriteServiceTests
{
    [Fact]
    public async Task FavouriteAsync_ShouldStoreRow_AndReportFavourited()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();

        var response = await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        response.EventId.Should().Be(harness.EventId);
        response.IsFavourited.Should().BeTrue();

        var stored = await harness.Db.EventFavourites.AsNoTracking().SingleAsync();
        stored.EventId.Should().Be(harness.EventId);
        stored.UserId.Should().Be(harness.UserId);
    }

    [Fact]
    public async Task FavouriteAsync_ShouldBeIdempotent_WhenAlreadyFavourited()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();

        var first = await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");
        // A star is a double-tappable control, so a repeat click must not surface a conflict.
        var second = await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        second.IsFavourited.Should().BeTrue();
        second.FavouritedAtUtc.Should().Be(first.FavouritedAtUtc, "the original row is returned, not a new one");
        (await harness.Db.EventFavourites.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task FavouriteAsync_ShouldThrow_WhenUserCannotViewEvent()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        harness.RevokeEventVisibility();

        var act = async () => await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        await act.Should().ThrowAsync<ResourceNotFoundException>();
        (await harness.Db.EventFavourites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UnfavouriteAsync_ShouldHardDeleteTheRow()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        await harness.Service.UnfavouriteAsync(harness.EventId, harness.UserId);

        (await harness.Db.EventFavourites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UnfavouriteAsync_ShouldBeNoOp_WhenNotFavourited()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();

        var act = async () => await harness.Service.UnfavouriteAsync(harness.EventId, harness.UserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnfavouriteAsync_ShouldSucceed_WhenEventVisibilityWasRevoked()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        // Losing access to a private event must not strand the star with no way to remove it.
        harness.RevokeEventVisibility();
        harness.DenyAccessFor(harness.UserId);

        await harness.Service.UnfavouriteAsync(harness.EventId, harness.UserId);

        (await harness.Db.EventFavourites.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FavouriteThenUnfavouriteThenFavourite_ShouldSucceed()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();

        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");
        await harness.Service.UnfavouriteAsync(harness.EventId, harness.UserId);
        var restarred = await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        // This is the backtrack path the pinned page relies on: unstar, change your mind, re-star.
        restarred.IsFavourited.Should().BeTrue();
        (await harness.Db.EventFavourites.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetFavouriteEventIdsAsync_ShouldReturnOnlyTheCallersStars()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");
        await harness.Service.FavouriteAsync(harness.SoonEventId, harness.UserId, "Participant");
        await harness.Service.FavouriteAsync(harness.EventId, harness.OtherUserId, "Participant");

        var ids = await harness.Service.GetFavouriteEventIdsAsync(harness.UserId);

        ids.Should().BeEquivalentTo([harness.EventId, harness.SoonEventId]);
    }

    [Fact]
    public async Task IsFavouritedAsync_ShouldTrackTheStar()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();

        (await harness.Service.IsFavouritedAsync(harness.EventId, harness.UserId)).Should().BeFalse();

        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        (await harness.Service.IsFavouritedAsync(harness.EventId, harness.UserId)).Should().BeTrue();
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldReturnEmpty_WhenNothingPinned()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();

        (await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant")).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldUnionRegistrationsAndFavourites()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        // Registered but never starred — it must still appear.
        await harness.RegisterAsync(harness.UserId, harness.EventId);
        // Starred but not registered.
        await harness.Service.FavouriteAsync(harness.SoonEventId, harness.UserId, "Participant");

        var pinned = await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant");

        pinned.Should().HaveCount(2);
        pinned.Single(p => p.Event.Id == harness.EventId).IsRegistered.Should().BeTrue();
        pinned.Single(p => p.Event.Id == harness.EventId).IsFavourited.Should().BeFalse();
        pinned.Single(p => p.Event.Id == harness.SoonEventId).IsRegistered.Should().BeFalse();
        pinned.Single(p => p.Event.Id == harness.SoonEventId).IsFavourited.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldFlagBothSignals_WhenRegisteredAndFavourited()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.RegisterAsync(harness.UserId, harness.EventId);
        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        var row = (await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant")).Should().ContainSingle().Subject;

        row.IsRegistered.Should().BeTrue();
        row.IsFavourited.Should().BeTrue();
        row.RegisteredAtUtc.Should().NotBeNull();
        row.FavouritedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldOrderRegisteredFirst_ThenByStartTime()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        // Registered on the event that starts LAST, starred on the two that start sooner —
        // so a pure start-time sort would put the registered row at the bottom.
        await harness.RegisterAsync(harness.UserId, harness.LateEventId);
        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");
        await harness.Service.FavouriteAsync(harness.SoonEventId, harness.UserId, "Participant");

        var pinned = await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant");

        pinned.Select(p => p.Event.Id).Should().Equal(
            harness.LateEventId,   // Going
            harness.SoonEventId,   // Saved, starts in 1 day
            harness.EventId);      // Saved, starts in 2 days
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldIgnoreCancelledRegistrations()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.RegisterAsync(harness.UserId, harness.EventId, RegistrationStatus.Cancelled);

        (await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant")).Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldRedactButKeepRows_WhenAccessWasRevoked()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.Service.FavouriteAsync(harness.EventId, harness.UserId, "Participant");

        harness.DenyAccessFor(harness.UserId);

        var row = (await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant")).Should().ContainSingle().Subject;

        row.AccessRevoked.Should().BeTrue();
        row.Event.Name.Should().BeEmpty();
        row.Event.Location.Should().BeEmpty();

        // ...but the row survives, or the user has no way to remove the star.
        row.Event.Id.Should().Be(harness.EventId);
        row.IsFavourited.Should().BeTrue();
    }

    [Fact]
    public async Task GetMyPinnedAsync_ShouldNotLeakOtherUsersPins()
    {
        await using var harness = await FavouriteServiceHarness.CreateAsync();
        await harness.Service.FavouriteAsync(harness.EventId, harness.OtherUserId, "Participant");
        await harness.RegisterAsync(harness.OtherUserId, harness.SoonEventId);

        (await harness.Service.GetMyPinnedAsync(harness.UserId, "Participant")).Should().BeEmpty();
    }
}

internal sealed class FavouriteServiceHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Mock<IEventsService> _eventsServiceMock;
    private readonly HashSet<int> _deniedUserIds = [];

    public AppDatabaseContext Db { get; }
    public EventFavouriteService Service { get; }

    /// <summary>Starts in 2 days.</summary>
    public int EventId => 1;
    /// <summary>Starts in 1 day — the earliest of the three.</summary>
    public int SoonEventId => 2;
    /// <summary>Starts in 5 days — the latest of the three.</summary>
    public int LateEventId => 3;
    public int OrganizerUserId => 1;
    public int UserId => 2;
    public int OtherUserId => 3;

    private FavouriteServiceHarness(
        SqliteConnection connection,
        AppDatabaseContext db,
        EventFavouriteService service,
        Mock<IEventsService> eventsServiceMock)
    {
        _connection = connection;
        Db = db;
        Service = service;
        _eventsServiceMock = eventsServiceMock;
    }

    public static async Task<FavouriteServiceHarness> CreateAsync()
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
            new User { Id = 3, Email = "three@test.local", Name = "Three", Usertype = "Participant" });

        db.Clubs.Add(new Club
        {
            Id = 1,
            UserId = 1,
            Name = "Favourites Club",
            Description = "Favourites coverage club",
            Clubtype = ClubType.Gaming,
            ClubImage = "https://cdn.test/clubs/favourites.png"
        });

        var now = DateTime.UtcNow;
        db.Events.AddRange(
            NewEvent(1, "Starred Event", now.AddDays(2)),
            NewEvent(2, "Sooner Event", now.AddDays(1)),
            NewEvent(3, "Later Event", now.AddDays(5)));

        await db.SaveChangesAsync();

        var eventsServiceMock = new Mock<IEventsService>();
        eventsServiceMock
            .Setup(service => service.EnsureCanViewEventAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var refreshCacheMock = new Mock<IRefreshAheadCache>();
        refreshCacheMock.Setup(cache => cache.RemoveAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var harnessRef = new FavouriteServiceHarness[1];

        var accessCheckerMock = new Mock<IEventAccessChecker>();
        accessCheckerMock
            .Setup(checker => checker.CanViewEventAsync(
                It.IsAny<backend.main.features.events.Events>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync((backend.main.features.events.Events _, int? userId, string? _) =>
                harnessRef[0] == null || userId == null || !harnessRef[0]._deniedUserIds.Contains(userId.Value));

        var service = new EventFavouriteService(
            db,
            new EventFavouriteRepository(db),
            eventsServiceMock.Object,
            accessCheckerMock.Object,
            refreshCacheMock.Object);

        var harness = new FavouriteServiceHarness(connection, db, service, eventsServiceMock);
        harnessRef[0] = harness;

        return harness;
    }

    private static backend.main.features.events.Events NewEvent(int id, string name, DateTime startTime) => new()
    {
        Id = id,
        ClubId = 1,
        Name = name,
        Description = "An event used for favourite service tests.",
        Location = "Student Center",
        LifecycleState = EventLifecycleState.Published,
        StartTime = startTime,
        EndTime = startTime.AddHours(2),
        maxParticipants = 10,
        registerCost = 0,
        Category = EventCategory.Other
    };

    public async Task RegisterAsync(int userId, int eventId, RegistrationStatus status = RegistrationStatus.Active)
    {
        Db.EventRegistrations.Add(new EventRegistration
        {
            EventId = eventId,
            UserId = userId,
            Status = status,
            CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>Makes the events service reject reads of the event, as a revocation would.</summary>
    public void RevokeEventVisibility() =>
        _eventsServiceMock
            .Setup(service => service.EnsureCanViewEventAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new ResourceNotFoundException("Event not found"));

    public void DenyAccessFor(int userId) => _deniedUserIds.Add(userId);

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
