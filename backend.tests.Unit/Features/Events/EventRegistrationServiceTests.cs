using System.Data;

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

namespace backend.tests.Unit.Features.Events;

public class EventRegistrationServiceTests
{
    [Fact]
    public async Task RegisterAsync_ShouldCreateRegistration_UpdateCount_AndInvalidateCaches()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        harness.EventIndexMembers = ["evtreg:list:e:1:1:20"];
        harness.UserIndexMembers = ["evtreg:list:u:2:1:20"];

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
        registration.Status.Should().Be(RegistrationStatus.Active);
        registration.Notes.Should().Be("bringing snacks");
        registration.PhoneNumber.Should().BeNull();
        registration.DietaryNeeds.Should().Be("vegan");

        var trackedEvent = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        trackedEvent.RegistrationCount.Should().Be(1);

        harness.OutboxWriterMock.Verify(writer => writer.StageSync(
            It.Is<backend.main.features.events.Events>(ev => ev.Id == harness.EventId && ev.RegistrationCount == 1)),
            Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync(MembershipKey(harness.ParticipantUserId, harness.EventId)), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync($"event:{harness.EventId}"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:e:1:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.DeleteKeyAsync("evtreg:list:u:2:1:20"), Times.Once);
        harness.CacheMock.Verify(cache => cache.ReleaseLockAsync(LockKey(harness.EventId), It.IsAny<string>()), Times.Once);
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

    [Fact]
    public async Task RegisterAsync_ShouldReactivateCancelledRegistration()
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
            new RegisterEventRequest { Notes = "  refreshed  " });

        var registrations = await harness.Db.EventRegistrations
            .Where(r => r.EventId == harness.EventId && r.UserId == harness.ParticipantUserId)
            .ToListAsync();

        registrations.Should().ContainSingle();
        registrations[0].Id.Should().Be(existing.Id);
        registrations[0].Status.Should().Be(RegistrationStatus.Active);
        registrations[0].CancelledAt.Should().BeNull();
        registrations[0].Notes.Should().Be("refreshed");
    }

    [Fact]
    public async Task UnregisterAsync_ShouldSoftCancelRegistration_UpdateCount_AndInvalidateCaches()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        harness.EventIndexMembers = ["evtreg:list:e:1:1:20"];
        harness.UserIndexMembers = ["evtreg:list:u:2:1:20"];
        await harness.SeedRegistrationAsync(harness.ParticipantUserId, RegistrationStatus.Active);

        await harness.Service.UnregisterAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole);

        var registration = await harness.Db.EventRegistrations.SingleAsync(r =>
            r.EventId == harness.EventId && r.UserId == harness.ParticipantUserId);
        registration.Status.Should().Be(RegistrationStatus.Cancelled);
        registration.CancelledAt.Should().NotBeNull();

        var trackedEvent = await harness.Db.Events.SingleAsync(e => e.Id == harness.EventId);
        trackedEvent.RegistrationCount.Should().Be(0);
        harness.OutboxWriterMock.Verify(writer => writer.StageSync(It.IsAny<backend.main.features.events.Events>()), Times.Once);
        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync(MembershipKey(harness.ParticipantUserId, harness.EventId)), Times.Once);
    }

    [Fact]
    public async Task UpdateRegistrationAsync_ShouldSanitizeValues_AndInvalidateCaches()
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

        harness.RefreshCacheMock.Verify(cache => cache.RemoveAsync(MembershipKey(harness.ParticipantUserId, harness.EventId)), Times.Once);
        harness.CacheMock.Verify(cache => cache.ReleaseLockAsync(UpdateLockKey(harness.ParticipantUserId, harness.EventId), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task IsRegisteredAsync_AndGetMyRegistrationAsync_ShouldUseMembershipCache()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var active = new EventRegistration
        {
            EventId = harness.EventId,
            UserId = harness.ParticipantUserId,
            Status = RegistrationStatus.Active
        };

        harness.RefreshCacheMock
            .Setup(cache => cache.GetOrSetAsync(
                MembershipKey(harness.ParticipantUserId, harness.EventId),
                It.IsAny<Func<Task<EventRegistration?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(active);

        (await harness.Service.IsRegisteredAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole)).Should().BeTrue();
        (await harness.Service.GetMyRegistrationAsync(harness.EventId, harness.ParticipantUserId, harness.ParticipantRole)).Should().BeSameAs(active);
    }

    [Fact]
    public async Task GetRegistrationsByEventAsync_AndGetRegistrationsByUserAsync_ShouldTrackIndexKeys()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var eventRegistrations = new List<EventRegistration> { new() { EventId = harness.EventId, UserId = harness.ParticipantUserId, Status = RegistrationStatus.Active } };
        var userRegistrations = new List<EventRegistration> { new() { EventId = harness.EventId, UserId = harness.ParticipantUserId, Status = RegistrationStatus.Active } };

        harness.RefreshCacheMock
            .Setup(cache => cache.GetOrSetAsync(
                "evtreg:list:e:1:2:5",
                It.IsAny<Func<Task<List<EventRegistration>?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(eventRegistrations);
        harness.RefreshCacheMock
            .Setup(cache => cache.GetOrSetAsync(
                "evtreg:list:u:2:3:4",
                It.IsAny<Func<Task<List<EventRegistration>?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(userRegistrations);

        (await harness.Service.GetRegistrationsByEventAsync(harness.EventId, 2, 5)).Should().BeEquivalentTo(eventRegistrations);
        (await harness.Service.GetRegistrationsByUserAsync(harness.ParticipantUserId, 3, 4)).Should().BeEquivalentTo(userRegistrations);

        harness.CacheMock.Verify(cache => cache.SetAddAsync(EventIndexKey(harness.EventId), "evtreg:list:e:1:2:5"), Times.Once);
        harness.CacheMock.Verify(cache => cache.SetAddAsync(UserIndexKey(harness.ParticipantUserId), "evtreg:list:u:2:3:4"), Times.Once);
        harness.CacheMock.Verify(cache => cache.SetExpiryAsync(EventIndexKey(harness.EventId), It.IsAny<TimeSpan>()), Times.Once);
        harness.CacheMock.Verify(cache => cache.SetExpiryAsync(UserIndexKey(harness.ParticipantUserId), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task BatchRegisterAsync_AndBatchUnregisterAsync_ShouldAggregateFailures()
    {
        await using var harness = await EventRegistrationHarness.CreateAsync();
        var secondEventId = await harness.SeedEventAsync(
            name: "Paid Event",
            registerCost: 1000,
            maxParticipants: 10);

        var registerResult = await harness.Service.BatchRegisterAsync(
            harness.ParticipantUserId,
            harness.ParticipantRole,
            [harness.EventId, secondEventId]);

        registerResult.Succeeded.Should().Contain(harness.EventId);
        registerResult.Failed.Should().ContainSingle(f => f.EventId == secondEventId && f.Reason.Contains("Paid events require checkout"));

        var unregisterResult = await harness.Service.BatchUnregisterAsync(
            harness.ParticipantUserId,
            harness.ParticipantRole,
            [harness.EventId, secondEventId]);

        unregisterResult.Succeeded.Should().Contain(harness.EventId);
        unregisterResult.Failed.Should().ContainSingle(f => f.EventId == secondEventId && f.Reason.Contains("Registration not found"));
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
                Description = "Coverage club",
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
            cacheMock
                .Setup(cache => cache.SetAddAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            cacheMock
                .Setup(cache => cache.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
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

        public async Task<int> SeedEventAsync(string name, int registerCost, int maxParticipants)
        {
            var ev = new backend.main.features.events.Events
            {
                ClubId = 1,
                Name = name,
                Description = "Additional event",
                Location = "North Hall",
                LifecycleState = EventLifecycleState.Published,
                StartTime = DateTime.UtcNow.AddDays(3),
                EndTime = DateTime.UtcNow.AddDays(3).AddHours(2),
                maxParticipants = maxParticipants,
                registerCost = registerCost,
                Category = EventCategory.Other
            };
            Db.Events.Add(ev);
            await Db.SaveChangesAsync();
            return ev.Id;
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
