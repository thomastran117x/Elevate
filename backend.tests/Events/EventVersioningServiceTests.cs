using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.analytics;
using backend.main.features.events.contracts.requests;
using backend.main.features.events.images;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace backend.tests.Events;

public class EventVersioningServiceTests
{
    [Fact]
    public async Task CreateEvent_ShouldCreateInitialVersionWithActorMetadata()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        harness.SeedUploadIntent(PrimaryImageUrl, clubId: 4, userId: 7);

        var ev = await harness.Service.CreateEvent(
            clubId: 4,
            userId: 7,
            name: "Board Game Night",
            description: "A casual game night with strategy tables.",
            location: "Student Center",
            imageUrls: [PrimaryImageUrl],
            startTime: harness.TimeProvider.GetUtcNow().UtcDateTime.AddDays(2),
            endTime: harness.TimeProvider.GetUtcNow().UtcDateTime.AddDays(2).AddHours(3),
            isPrivate: false,
            maxParticipants: 40,
            registerCost: 0,
            category: EventCategory.Gaming,
            venueName: "Room 201",
            city: "Toronto",
            latitude: 43.6532,
            longitude: -79.3832,
            tags: ["Games", "Campus"]);

        ev.CurrentVersionNumber.Should().Be(1);

        var version = await harness.Db.EventVersions.SingleAsync();
        var snapshot = JsonSerializer.Deserialize<EventVersionSnapshot>(version.SnapshotJson);
        var changes = JsonSerializer.Deserialize<List<EventVersionFieldChange>>(version.ChangedFieldsJson);

        version.VersionNumber.Should().Be(1);
        version.ActionType.Should().Be(EventVersionActions.Create);
        version.ActorUserId.Should().Be(7);
        version.ActorRole.Should().Be("Organizer");
        snapshot.Should().NotBeNull();
        snapshot!.Name.Should().Be("Board Game Night");
        snapshot.Tags.Should().BeEquivalentTo(["games", "campus"]);
        changes.Should().Contain(change => change.Field == "name" && change.NewValue == "Board Game Night");
    }

    [Fact]
    public async Task UpdateEvent_ShouldCreateVersionAndIncrementCurrentVersion()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        var created = await harness.CreateEventAsync();

        var updated = await harness.Service.UpdateEvent(
            created.Id,
            userId: 7,
            name: "Advanced Board Game Night",
            description: "Competitive strategy tables and a newcomer pod.",
            location: "Innovation Hall",
            imageUrls: null,
            startTime: created.StartTime.AddDays(1),
            endTime: created.EndTime?.AddDays(1),
            isPrivate: true,
            maxParticipants: 60,
            registerCost: 15,
            category: EventCategory.Social,
            venueName: "Auditorium",
            city: "Mississauga",
            latitude: 43.5890,
            longitude: -79.6441,
            tags: ["Night", "Strategy"]);

        updated.CurrentVersionNumber.Should().Be(2);

        var versions = await harness.Db.EventVersions
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();

        versions.Should().HaveCount(2);
        versions[1].ActionType.Should().Be(EventVersionActions.Update);
        JsonSerializer.Deserialize<List<EventVersionFieldChange>>(versions[1].ChangedFieldsJson)!
            .Should()
            .Contain(change => change.Field == "name" && change.NewValue == "Advanced Board Game Night");
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldRestoreCoreFieldsOnly_AndKeepRegistrationCount()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        var created = await harness.CreateEventAsync();

        harness.TimeProvider.Advance(TimeSpan.FromDays(5));

        await harness.Service.UpdateEvent(
            created.Id,
            userId: 7,
            name: "Advanced Board Game Night",
            description: "Competitive strategy tables and a newcomer pod.",
            location: "Innovation Hall",
            imageUrls: null,
            startTime: created.StartTime.AddDays(1),
            endTime: created.EndTime?.AddDays(1),
            isPrivate: true,
            maxParticipants: 60,
            registerCost: 15,
            category: EventCategory.Social,
            venueName: "Auditorium",
            city: "Mississauga",
            latitude: 43.5890,
            longitude: -79.6441,
            tags: ["Night", "Strategy"]);

        var liveEvent = await harness.Db.Events.SingleAsync();
        liveEvent.RegistrationCount = 12;
        await harness.Db.SaveChangesAsync();

        var rollback = await harness.Service.RollbackToVersionAsync(
            created.Id,
            versionNumber: 1,
            userId: 7,
            userRole: "Organizer");

        rollback.NewVersionNumber.Should().Be(3);

        var reloaded = await harness.Db.Events.SingleAsync();
        reloaded.Name.Should().Be("Board Game Night");
        reloaded.Description.Should().Be("A casual game night with strategy tables.");
        reloaded.Location.Should().Be("Student Center");
        reloaded.Category.Should().Be(EventCategory.Gaming);
        reloaded.RegistrationCount.Should().Be(12);
        reloaded.CurrentVersionNumber.Should().Be(3);

        var latestVersion = await harness.Db.EventVersions
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();

        latestVersion.ActionType.Should().Be(EventVersionActions.Rollback);
        latestVersion.RollbackSourceVersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldAllowAdmin()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        var created = await harness.CreateEventAsync();

        await harness.Service.UpdateEvent(
            created.Id,
            userId: 7,
            name: "Admin Rollback Target",
            description: "A changed version that admins can revert.",
            location: "Main Hall",
            imageUrls: null,
            startTime: created.StartTime.AddDays(1),
            endTime: created.EndTime?.AddDays(1),
            isPrivate: false,
            maxParticipants: 55,
            registerCost: 10,
            category: EventCategory.Workshop,
            venueName: "Main Hall",
            city: "Toronto",
            latitude: 43.7,
            longitude: -79.4,
            tags: ["admin"]);

        var rollback = await harness.Service.RollbackToVersionAsync(
            created.Id,
            versionNumber: 1,
            userId: 99,
            userRole: "Admin");

        rollback.NewVersionNumber.Should().Be(3);
        rollback.Event.Name.Should().Be("Board Game Night");

        var latestVersion = await harness.Db.EventVersions
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();

        latestVersion.ActorUserId.Should().Be(99);
        latestVersion.ActorRole.Should().Be("Admin");
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldRejectExpiredVersion()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        var created = await harness.CreateEventAsync();

        harness.TimeProvider.Advance(TimeSpan.FromDays(91));

        await harness.Service.UpdateEvent(
            created.Id,
            userId: 7,
            name: "Expired Rollback Target",
            description: "Changed after the rollback window elapsed.",
            location: "Main Hall",
            imageUrls: null,
            startTime: created.StartTime.AddDays(1),
            endTime: created.EndTime?.AddDays(1),
            isPrivate: false,
            maxParticipants: 55,
            registerCost: 10,
            category: EventCategory.Workshop,
            venueName: "Main Hall",
            city: "Toronto",
            latitude: 43.7,
            longitude: -79.4,
            tags: ["expired"]);

        var act = () => harness.Service.RollbackToVersionAsync(
            created.Id,
            1,
            7,
            "Organizer");

        await act.Should()
            .ThrowAsync<BadRequestException>()
            .WithMessage("This version is no longer eligible for rollback.");
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ShouldRejectNonOwnerNonAdmin()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        var created = await harness.CreateEventAsync();

        var act = () => harness.Service.GetVersionHistoryAsync(
            created.Id,
            userId: 55,
            userRole: "Participant");

        await act.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task BatchCreateAndBatchUpdate_ShouldRecordVersionsForEachEvent()
    {
        await using var harness = await EventServiceHarness.CreateAsync();
        harness.SeedUploadIntent(PrimaryImageUrl, clubId: 4, userId: 7);
        harness.SeedUploadIntent(SecondaryImageUrl, clubId: 4, userId: 7);

        var created = await harness.Service.BatchCreateEvents(
            clubId: 4,
            userId: 7,
            [
                new BatchCreateEventItem
                {
                    Name = "Event One",
                    Description = "The first event in the batch create flow.",
                    Location = "Room A",
                    ImageUrls = [PrimaryImageUrl],
                    StartTime = harness.TimeProvider.GetUtcNow().UtcDateTime.AddDays(3),
                    EndTime = harness.TimeProvider.GetUtcNow().UtcDateTime.AddDays(3).AddHours(2),
                    Category = EventCategory.Academic
                },
                new BatchCreateEventItem
                {
                    Name = "Event Two",
                    Description = "The second event in the batch create flow.",
                    Location = "Room B",
                    ImageUrls = [SecondaryImageUrl],
                    StartTime = harness.TimeProvider.GetUtcNow().UtcDateTime.AddDays(4),
                    EndTime = harness.TimeProvider.GetUtcNow().UtcDateTime.AddDays(4).AddHours(2),
                    Category = EventCategory.Workshop
                }
            ]);

        created.Created.Should().HaveCount(2);
        (await harness.Db.EventVersions.CountAsync()).Should().Be(2);

        var updatedCount = await harness.Service.BatchUpdateEvents(
            7,
            [
                new BatchUpdateEventItem
                {
                    EventId = created.Created[0].Id,
                    Name = "Event One Updated",
                    MaxParticipants = 75
                },
                new BatchUpdateEventItem
                {
                    EventId = created.Created[1].Id,
                    IsPrivate = true,
                    City = "Toronto"
                }
            ]);

        updatedCount.Should().Be(2);

        var events = await harness.Db.Events
            .OrderBy(e => e.Id)
            .ToListAsync();

        events.Should().OnlyContain(e => e.CurrentVersionNumber == 2);
        (await harness.Db.EventVersions.CountAsync()).Should().Be(4);
        (await harness.Db.EventVersions.CountAsync(v => v.ActionType == EventVersionActions.Update)).Should().Be(2);
    }

    private const string PrimaryImageUrl = "https://cdn.test/events/event-v1.png";
    private const string SecondaryImageUrl = "https://cdn.test/events/event-v2.png";

    private sealed class EventServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly Dictionary<string, string?> _cacheStore;

        public AppDatabaseContext Db { get; }
        public EventsService Service { get; }
        public TestTimeProvider TimeProvider { get; }

        private EventServiceHarness(
            SqliteConnection connection,
            AppDatabaseContext db,
            EventsService service,
            TestTimeProvider timeProvider,
            Dictionary<string, string?> cacheStore)
        {
            _connection = connection;
            Db = db;
            Service = service;
            TimeProvider = timeProvider;
            _cacheStore = cacheStore;
        }

        public static async Task<EventServiceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var dbOptions = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(dbOptions);
            await db.Database.EnsureCreatedAsync();

            var now = new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero);
            db.Users.AddRange(
                new User
                {
                    Id = 7,
                    Email = "owner@test.local",
                    Usertype = "Organizer",
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime
                },
                new User
                {
                    Id = 99,
                    Email = "admin@test.local",
                    Usertype = "Admin",
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime
                });
            db.Clubs.Add(new Club
            {
                Id = 4,
                UserId = 7,
                Name = "Games Club",
                Description = "A club for tabletop and social games.",
                Clubtype = ClubType.Gaming,
                ClubImage = "https://cdn.test/clubs/games.png",
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime
            });
            await db.SaveChangesAsync();

            var timeProvider = new TestTimeProvider(now);
            var club = await db.Clubs.SingleAsync(c => c.Id == 4);

            var cache = new Mock<ICacheService>();
            var cacheStore = new Dictionary<string, string?>(StringComparer.Ordinal);
            cache.Setup(service => service.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync((string key, string value, TimeSpan? _) =>
                {
                    cacheStore[key] = value;
                    return true;
                });
            cache.Setup(service => service.GetValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) => cacheStore.TryGetValue(key, out var value) ? value : null);
            cache.Setup(service => service.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(1L);
            cache.Setup(service => service.GetManyAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string?>());
            cache.Setup(service => service.DeleteKeyAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) =>
                {
                    cacheStore.Remove(key);
                    return true;
                });
            cache.Setup(service => service.KeyExistsAsync(It.IsAny<string>()))
                .ReturnsAsync((string key) => cacheStore.ContainsKey(key));
            cache.Setup(service => service.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            var clubService = new Mock<IClubService>();
            clubService.Setup(service => service.GetClub(4))
                .ReturnsAsync(club);
            clubService.Setup(service => service.GetClubByUser(7))
                .ReturnsAsync(club);

            var blobService = new Mock<IAzureBlobService>();
            blobService.Setup(service => service.IsOwnedBlobUrl(It.IsAny<string>()))
                .Returns(true);
            blobService.Setup(service => service.DeleteBlobAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var service = new EventsService(
                db,
                new EventsRepository(db),
                new EventImageRepository(db),
                clubService.Object,
                blobService.Object,
                cache.Object,
                Mock.Of<IEventAnalyticsRepository>(),
                Mock.Of<IEventSearchService>(),
                Mock.Of<IEventSearchOutboxWriter>(),
                Mock.Of<IEventRegistrationRepository>(),
                Options.Create(new EventVersioningOptions
                {
                    RollbackWindowDays = 90
                }),
                timeProvider);

            return new EventServiceHarness(connection, db, service, timeProvider, cacheStore);
        }

        public async Task<backend.main.features.events.Events> CreateEventAsync()
        {
            SeedUploadIntent(PrimaryImageUrl, clubId: 4, userId: 7);

            return await Service.CreateEvent(
                clubId: 4,
                userId: 7,
                name: "Board Game Night",
                description: "A casual game night with strategy tables.",
                location: "Student Center",
                imageUrls: [PrimaryImageUrl],
                startTime: TimeProvider.GetUtcNow().UtcDateTime.AddDays(2),
                endTime: TimeProvider.GetUtcNow().UtcDateTime.AddDays(2).AddHours(3),
                isPrivate: false,
                maxParticipants: 40,
                registerCost: 0,
                category: EventCategory.Gaming,
                venueName: "Room 201",
                city: "Toronto",
                latitude: 43.6532,
                longitude: -79.3832,
                tags: ["Games", "Campus"]);
        }

        public void SeedUploadIntent(string imageUrl, int clubId, int userId, int? eventId = null)
        {
            _cacheStore[GetImageUploadIntentKey(imageUrl)] = JsonSerializer.Serialize(new
            {
                ClubId = clubId,
                EventId = eventId,
                UserId = userId,
                PublicUrl = imageUrl,
                ContentType = "image/png"
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static string GetImageUploadIntentKey(string imageUrl)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl));
            return $"event:image-upload:intent:{Convert.ToHexString(bytes)}";
        }
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan by)
        {
            _utcNow = _utcNow.Add(by);
        }
    }
}
