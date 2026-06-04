using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.registration;
using backend.main.features.events.registration.contracts.requests;
using backend.main.features.events.search;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

namespace backend.tests.Integration.Features.Events;

public class EventRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_ShouldCreateRegistration_UpdateCount_StageSync_AndInvalidateCaches()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        harness.EventIndexMembers = ["evtreg:list:e:1:1:20"];
        harness.UserIndexMembers = ["evtreg:list:u:2:1:20"];

        await harness.Service.RegisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        var registration = await harness.Db.EventRegistrations.SingleAsync(r =>
            r.EventId == harness.EventId &&
            r.UserId == harness.ParticipantUserId);
        registration.Status.Should().Be(RegistrationStatus.Active);

        var trackedEvent = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        trackedEvent.RegistrationCount.Should().Be(1);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(ev =>
                ev.Id == harness.EventId &&
                ev.RegistrationCount == 1)),
            Times.Once);

        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync(MembershipKey(harness.ParticipantUserId, harness.EventId)), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{harness.EventId}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.SetMembersAsync(EventIndexKey(harness.EventId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.SetMembersAsync(UserIndexKey(harness.ParticipantUserId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:e:1:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:u:2:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync(EventIndexKey(harness.EventId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync(UserIndexKey(harness.ParticipantUserId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.ReleaseLockAsync(LockKey(harness.EventId), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_ShouldReactivateCancelledRegistration_InsteadOfInsertingDuplicate()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var existing = await harness.SeedRegistrationAsync(
            harness.ParticipantUserId,
            RegistrationStatus.Cancelled,
            notes: "old",
            cancelledAt: DateTime.UtcNow.AddMinutes(-10));

        await harness.Service.RegisterAsync(
            harness.EventId,
            harness.ParticipantUserId,
            harness.ParticipantRole,
            new RegisterEventRequest
            {
                Notes = "  refreshed  ",
                PhoneNumber = "  555-0100  "
            });

        var registrations = await harness.Db.EventRegistrations
            .Where(r => r.EventId == harness.EventId && r.UserId == harness.ParticipantUserId)
            .ToListAsync();

        registrations.Should().ContainSingle();
        registrations[0].Id.Should().Be(existing.Id);
        registrations[0].Status.Should().Be(RegistrationStatus.Active);
        registrations[0].CancelledAt.Should().BeNull();
        registrations[0].Notes.Should().Be("refreshed");
        registrations[0].PhoneNumber.Should().Be("555-0100");
    }

    [Fact]
    public async Task RegisterAsync_ShouldSanitizeOptionalFields()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();

        await harness.Service.RegisterAsync(
            harness.EventId,
            harness.ParticipantUserId,
            harness.ParticipantRole,
            new RegisterEventRequest
            {
                Notes = "  bringing snacks  ",
                PhoneNumber = "   ",
                DietaryNeeds = "  vegan  "
            });

        var registration = await harness.Db.EventRegistrations.SingleAsync(r =>
            r.EventId == harness.EventId &&
            r.UserId == harness.ParticipantUserId);

        registration.Notes.Should().Be("bringing snacks");
        registration.PhoneNumber.Should().BeNull();
        registration.DietaryNeeds.Should().Be("vegan");
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowConflict_WhenEventLockCannotBeAcquired()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        harness.CacheMock
            .Setup(cache => cache.AcquireLockAsync(LockKey(harness.EventId), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        var action = () => harness.Service.RegisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        await action.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("Event registration is busy, please try again");

        (await harness.Db.EventRegistrations.AnyAsync()).Should().BeFalse();
        harness.CacheMock.Verify(cache => cache.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowConflict_WhenUserAlreadyHasActiveRegistration()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        await harness.SeedRegistrationAsync(harness.ParticipantUserId, RegistrationStatus.Active);

        var action = () => harness.Service.RegisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        await action.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("Already registered for this event");
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowConflict_WhenEventIsFull()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var ev = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        ev.maxParticipants = 1;
        await harness.Db.SaveChangesAsync();

        await harness.SeedRegistrationAsync(harness.SecondParticipantUserId, RegistrationStatus.Active);

        var action = () => harness.Service.RegisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        await action.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("Event is full");
    }

    [Theory]
    [InlineData(-10, 10, "already started")]
    [InlineData(-20, -5, "already ended")]
    public async Task RegisterAsync_ShouldThrowCorrectConflict_ForStartedOrEndedEvents(
        int startOffsetMinutes,
        int endOffsetMinutes,
        string expectedFragment)
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var ev = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        ev.StartTime = DateTime.UtcNow.AddMinutes(startOffsetMinutes);
        ev.EndTime = DateTime.UtcNow.AddMinutes(endOffsetMinutes);
        await harness.Db.SaveChangesAsync();

        var action = () => harness.Service.RegisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        var exception = await action.Should().ThrowAsync<ConflictException>();
        exception.Which.Message.Should().Contain(expectedFragment);
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowBadRequest_ForPaidEvents()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var ev = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        ev.registerCost = 2500;
        await harness.Db.SaveChangesAsync();

        var action = () => harness.Service.RegisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        await action.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("Paid events require checkout");

        harness.CacheMock.Verify(cache => cache.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task UnregisterAsync_ShouldSoftCancelRegistration_UpdateCount_StageSync_AndInvalidateCaches()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        harness.EventIndexMembers = ["evtreg:list:e:1:1:20"];
        harness.UserIndexMembers = ["evtreg:list:u:2:1:20"];
        await harness.SeedRegistrationAsync(harness.ParticipantUserId, RegistrationStatus.Active);

        var ev = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        ev.RegistrationCount = 1;
        await harness.Db.SaveChangesAsync();

        await harness.Service.UnregisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        var registration = await harness.Db.EventRegistrations.SingleAsync(r =>
            r.EventId == harness.EventId &&
            r.UserId == harness.ParticipantUserId);
        registration.Status.Should().Be(RegistrationStatus.Cancelled);
        registration.CancelledAt.Should().NotBeNull();

        var trackedEvent = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        trackedEvent.RegistrationCount.Should().Be(0);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(entity =>
                entity.Id == harness.EventId &&
                entity.RegistrationCount == 0)),
            Times.Once);

        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync(MembershipKey(harness.ParticipantUserId, harness.EventId)), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{harness.EventId}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:e:1:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:u:2:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync(EventIndexKey(harness.EventId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync(UserIndexKey(harness.ParticipantUserId)), Times.Once);
    }

    [Fact]
    public async Task UnregisterAsync_ShouldThrowNotFound_WhenNoActiveRegistrationExists()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();

        var action = () => harness.Service.UnregisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        await action.Should()
            .ThrowAsync<ResourceNotFoundException>()
            .WithMessage("Registration not found");
    }

    [Fact]
    public async Task UpdateRegistrationAsync_ShouldUpdateMutableFields_SanitizeValues_AndInvalidateCaches()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        harness.EventIndexMembers = ["evtreg:list:e:1:1:20"];
        harness.UserIndexMembers = ["evtreg:list:u:2:1:20"];
        await harness.SeedRegistrationAsync(
            harness.ParticipantUserId,
            RegistrationStatus.Active,
            notes: "old note",
            phoneNumber: "111",
            dietaryNeeds: "none");

        var updated = await harness.Service.UpdateRegistrationAsync(
            harness.EventId,
            harness.ParticipantUserId,
            harness.ParticipantRole,
            new UpdateRegistrationRequest
            {
                Notes = "  updated note  ",
                PhoneNumber = "   ",
                DietaryNeeds = "  vegetarian  "
            });

        updated.Notes.Should().Be("updated note");
        updated.PhoneNumber.Should().BeNull();
        updated.DietaryNeeds.Should().Be("vegetarian");

        var persisted = await harness.Db.EventRegistrations.SingleAsync(r =>
            r.EventId == harness.EventId &&
            r.UserId == harness.ParticipantUserId);
        persisted.Notes.Should().Be("updated note");
        persisted.PhoneNumber.Should().BeNull();
        persisted.DietaryNeeds.Should().Be("vegetarian");

        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync(MembershipKey(harness.ParticipantUserId, harness.EventId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:e:1:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:u:2:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync(EventIndexKey(harness.EventId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync(UserIndexKey(harness.ParticipantUserId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.ReleaseLockAsync(UpdateLockKey(harness.ParticipantUserId, harness.EventId), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRegistrationAsync_ShouldThrowConflict_WhenUpdateLockCannotBeAcquired()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        await harness.SeedRegistrationAsync(harness.ParticipantUserId, RegistrationStatus.Active);
        harness.CacheMock
            .Setup(cache => cache.AcquireLockAsync(UpdateLockKey(harness.ParticipantUserId, harness.EventId), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);

        var action = () => harness.Service.UpdateRegistrationAsync(
            harness.EventId,
            harness.ParticipantUserId,
            harness.ParticipantRole,
            new UpdateRegistrationRequest());

        await action.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("Registration update is busy, please try again");

        harness.CacheMock.Verify(cache => cache.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static string LockKey(int eventId) => $"evtreg:lock:{eventId}";

    private static string UpdateLockKey(int userId, int eventId) => $"evtreg:update:u:{userId}:e:{eventId}";

    private static string MembershipKey(int userId, int eventId) => $"evtreg:u:{userId}:e:{eventId}";

    private static string EventIndexKey(int eventId) => $"evtreg:index:e:{eventId}";

    private static string UserIndexKey(int userId) => $"evtreg:index:u:{userId}";

    private sealed class EventRegistrationHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public EventRegistrationService Service { get; }
        public Mock<IEventsService> EventsServiceMock { get; }
        public Mock<ICacheService> CacheMock { get; }
        public Mock<IRefreshAheadCache> RefreshCacheMock { get; }
        public Mock<IEventSearchOutboxWriter> OutboxWriterMock { get; }
        public int EventId { get; }
        public int ParticipantUserId => 2;
        public int SecondParticipantUserId => 3;
        public string ParticipantRole => "Participant";
        public string[] EventIndexMembers { get; set; } = [];
        public string[] UserIndexMembers { get; set; } = [];

        private EventRegistrationHarness(
            SqliteConnection connection,
            AppDatabaseContext db,
            EventRegistrationService service,
            Mock<IEventsService> eventsServiceMock,
            Mock<ICacheService> cacheMock,
            Mock<IRefreshAheadCache> refreshCacheMock,
            Mock<IEventSearchOutboxWriter> outboxWriterMock,
            int eventId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            EventsServiceMock = eventsServiceMock;
            CacheMock = cacheMock;
            RefreshCacheMock = refreshCacheMock;
            OutboxWriterMock = outboxWriterMock;
            EventId = eventId;
        }

        public static async Task<EventRegistrationHarness> CreateAsync()
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
                new User { Id = 2, Email = "participant@test.local", Usertype = "Participant" },
                new User { Id = 3, Email = "participant-two@test.local", Usertype = "Participant" });

            db.Clubs.Add(new Club
            {
                Id = 1,
                UserId = 1,
                Name = "Events Club",
                Description = "Coverage-focused test club.",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/events.png"
            });

            db.Events.Add(new backend.main.features.events.Events
            {
                Id = 1,
                ClubId = 1,
                Name = "Future Event",
                Description = "A future published event for registration tests.",
                Location = "Student Center",
                LifecycleState = EventLifecycleState.Published,
                StartTime = DateTime.UtcNow.AddDays(2),
                EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
                maxParticipants = 10,
                registerCost = 0,
                Category = EventCategory.Other
            });

            await db.SaveChangesAsync();

            var eventsServiceMock = new Mock<IEventsService>();
            eventsServiceMock
                .Setup(service => service.EnsureCanViewEventAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            eventsServiceMock
                .Setup(service => service.GetEvent(It.IsAny<int>()))
                .ReturnsAsync((int eventId) => db.Events.AsNoTracking().Single(e => e.Id == eventId));

            var cacheMock = new Mock<ICacheService>();
            cacheMock
                .Setup(cache => cache.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);
            cacheMock
                .Setup(cache => cache.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            cacheMock
                .Setup(cache => cache.DeleteKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);

            var refreshCacheMock = new Mock<IRefreshAheadCache>();
            refreshCacheMock
                .Setup(cache => cache.RemoveAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var outboxWriterMock = new Mock<IEventSearchOutboxWriter>();

            EventRegistrationHarness? harness = null;
            cacheMock
                .Setup(cache => cache.SetMembersAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) =>
                {
                    if (harness == null)
                        return [];

                    if (key == EventIndexKey(harness.EventId))
                        return harness.EventIndexMembers;

                    if (key == UserIndexKey(harness.ParticipantUserId))
                        return harness.UserIndexMembers;

                    return [];
                });

            var service = new EventRegistrationService(
                db,
                new EventRegistrationRepository(db),
                eventsServiceMock.Object,
                cacheMock.Object,
                refreshCacheMock.Object,
                outboxWriterMock.Object);

            harness = new EventRegistrationHarness(
                connection,
                db,
                service,
                eventsServiceMock,
                cacheMock,
                refreshCacheMock,
                outboxWriterMock,
                1);

            return harness;
        }

        public async Task<EventRegistration> SeedRegistrationAsync(
            int userId,
            RegistrationStatus status,
            string? notes = null,
            string? phoneNumber = null,
            string? dietaryNeeds = null,
            DateTime? cancelledAt = null)
        {
            var registration = new EventRegistration
            {
                EventId = EventId,
                UserId = userId,
                Status = status,
                Notes = notes,
                PhoneNumber = phoneNumber,
                DietaryNeeds = dietaryNeeds,
                CancelledAt = cancelledAt
            };

            Db.EventRegistrations.Add(registration);
            await Db.SaveChangesAsync();
            return registration;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
