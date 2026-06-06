using backend.main.features.clubs;
using backend.main.features.events;
using backend.main.features.events.images;
using backend.main.features.events.search;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using EventEntity = backend.main.features.events.Events;

namespace backend.tests.Unit.Features.Events;

public class EventsRepositoryTests
{
    [Fact]
    public async Task CreateAsync_AndGetByIdAsync_ShouldPersistEventWithOrderedImages()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var createdAt = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var ev = harness.BuildEvent(
            "Launch Party",
            createdAt,
            images:
            [
                new EventImage { ImageUrl = "https://cdn.test/late.png", SortOrder = 2 },
                new EventImage { ImageUrl = "https://cdn.test/first.png", SortOrder = 1 }
            ]);

        await harness.Repository.CreateAsync(ev);
        await harness.Db.SaveChangesAsync();

        var loaded = await harness.Repository.GetByIdAsync(ev.Id);

        loaded.Should().NotBeNull();
        loaded!.Images.Should().HaveCount(2);
        loaded.Images.OrderBy(image => image.SortOrder).Select(image => image.ImageUrl)
            .Should().Equal("https://cdn.test/first.png", "https://cdn.test/late.png");
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnNewestEvents_FirstAndRespectPaging()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        harness.Db.Events.AddRange(
            harness.BuildEvent("Old", new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc)),
            harness.BuildEvent("Middle", new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc)),
            harness.BuildEvent("New", new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc)));
        await harness.Db.SaveChangesAsync();

        var page = await harness.Repository.GetAllAsync(page: 1, pageSize: 2);

        page.Select(ev => ev.Name).Should().Equal("New", "Middle");
    }

    [Fact]
    public async Task UpdateAsync_ShouldReplaceEditableFields_AndLoadImages()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var existing = harness.BuildEvent("Original", DateTime.UtcNow.AddDays(1));
        harness.Db.Events.Add(existing);
        await harness.Db.SaveChangesAsync();

        var updated = harness.BuildEvent("Updated", DateTime.UtcNow.AddDays(2));
        updated.Description = "Updated description";
        updated.Location = "New Hall";
        updated.isPrivate = true;
        updated.maxParticipants = 120;
        updated.registerCost = 15;
        updated.LifecycleState = EventLifecycleState.Published;
        updated.Category = EventCategory.Workshop;
        updated.VenueName = "Room 202";
        updated.City = "Toronto";
        updated.Latitude = 43.65;
        updated.Longitude = -79.38;
        updated.Tags = ["tech", "community"];
        updated.CurrentVersionNumber = 4;

        var result = await harness.Repository.UpdateAsync(existing.Id, updated);
        await harness.Db.SaveChangesAsync();

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.Tags.Should().Equal("tech", "community");
        result.CurrentVersionNumber.Should().Be(4);
        result.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdatePartialAsync_AndDeleteAsync_ShouldReturnExpectedFlags()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var ev = harness.BuildEvent("Patch Me", DateTime.UtcNow.AddDays(1));
        harness.Db.Events.Add(ev);
        await harness.Db.SaveChangesAsync();

        var patched = await harness.Repository.UpdatePartialAsync(ev.Id, item =>
        {
            item.Name = "Patched";
            item.registerCost = 25;
        });
        await harness.Db.SaveChangesAsync();

        patched.Should().BeTrue();
        (await harness.Repository.GetByIdAsync(ev.Id))!.Name.Should().Be("Patched");

        var deleted = await harness.Repository.DeleteAsync(ev.Id);
        await harness.Db.SaveChangesAsync();

        deleted.Should().BeTrue();
        (await harness.Repository.ExistsAsync(ev.Id)).Should().BeFalse();
        (await harness.Repository.UpdatePartialAsync(9999, _ => { })).Should().BeFalse();
        (await harness.Repository.DeleteAsync(9999)).Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyFilters_StatusAndPopularityOrdering()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var now = DateTime.UtcNow;
        harness.Db.Events.AddRange(
            harness.BuildEvent("Tech Summit", now.AddDays(3), city: "Ottawa", category: EventCategory.Workshop, registrationCount: 3),
            harness.BuildEvent("Tech Social", now.AddDays(2), city: "Ottawa", category: EventCategory.Workshop, registrationCount: 9),
            harness.BuildEvent("Past Tech", now.AddDays(-3), endTime: now.AddDays(-2), city: "Ottawa", category: EventCategory.Workshop, registrationCount: 50));
        await harness.Db.SaveChangesAsync();

        var (items, totalCount) = await harness.Repository.SearchAsync(new EventSearchCriteria
        {
            Query = "Tech",
            City = "Ottawa",
            Category = EventCategory.Workshop,
            Status = EventStatus.Upcoming,
            SortBy = EventSortBy.Popularity,
            Page = 1,
            PageSize = 10
        });

        totalCount.Should().Be(2);
        items.Select(item => item.Name).Should().Equal("Tech Social", "Tech Summit");
    }

    [Fact]
    public async Task SearchAsync_ShouldOrderByDistance_WhenCoordinatesAreProvided()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var now = DateTime.UtcNow;
        harness.Db.Events.AddRange(
            harness.BuildEvent("Far", now.AddDays(1), latitude: 46.0, longitude: -76.0),
            harness.BuildEvent("Near", now.AddDays(1), latitude: 45.4216, longitude: -75.6971),
            harness.BuildEvent("No Geo", now.AddDays(1), latitude: null, longitude: null));
        await harness.Db.SaveChangesAsync();

        var (items, totalCount) = await harness.Repository.SearchAsync(new EventSearchCriteria
        {
            Lat = 45.4215,
            Lng = -75.6972,
            RadiusKm = 500,
            SortBy = EventSortBy.Distance,
            Page = 1,
            PageSize = 10
        });

        totalCount.Should().Be(2);
        items.Select(item => item.Name).Should().Equal("Near", "Far");
    }

    [Fact]
    public async Task IncrementRegistrationCountAsync_ShouldClampAtZero()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var ev = harness.BuildEvent("Clamp Test", DateTime.UtcNow.AddDays(1), registrationCount: 2);
        harness.Db.Events.Add(ev);
        await harness.Db.SaveChangesAsync();

        await harness.Repository.IncrementRegistrationCountAsync(ev.Id, -10);

        await harness.Db.Entry(ev).ReloadAsync();
        var refreshed = ev;
        refreshed!.RegistrationCount.Should().Be(0);
    }

    [Fact]
    public async Task BatchMethods_ShouldCreateUpdateDeleteAndReturnDistinctIds()
    {
        await using var harness = await EventsRepositoryHarness.CreateAsync();
        var one = harness.BuildEvent("One", DateTime.UtcNow.AddDays(1));
        var two = harness.BuildEvent("Two", DateTime.UtcNow.AddDays(2));

        var created = await harness.Repository.CreateManyAsync([one, two]);
        created.Should().HaveCount(2);
        await harness.Db.SaveChangesAsync();

        var byIds = await harness.Repository.GetByIdsAsync([one.Id, one.Id, two.Id]);
        byIds.Should().HaveCount(2);

        var updatedCount = await harness.Repository.UpdateManyAsync(
        [
            (one.Id, ev => ev.Name = "One Updated"),
            (two.Id, ev => ev.Name = "Two Updated"),
            (9999, _ => { })
        ]);
        await harness.Db.SaveChangesAsync();

        updatedCount.Should().Be(2);
        (await harness.Repository.GetByIdAsync(one.Id))!.CurrentVersionNumber.Should().Be(1);

        var deletedCount = await harness.Repository.DeleteManyAsync([one.Id, one.Id, two.Id]);
        await harness.Db.SaveChangesAsync();

        deletedCount.Should().Be(2);
        (await harness.Repository.GetAllForReindexAsync(1, 10)).Should().BeEmpty();
    }

    private sealed class EventsRepositoryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public EventsRepository Repository { get; }
        public Club Club { get; }

        private EventsRepositoryHarness(SqliteConnection connection, AppDatabaseContext db, Club club)
        {
            _connection = connection;
            Db = db;
            Club = club;
            Repository = new EventsRepository(db);
        }

        public static async Task<EventsRepositoryHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            var owner = new User
            {
                Email = "owner@example.com",
                Usertype = "Organizer"
            };
            db.Users.Add(owner);
            await db.SaveChangesAsync();

            var club = new Club
            {
                Name = "Event Club",
                Description = "Hosts events.",
                Clubtype = ClubType.Social,
                ClubImage = "https://cdn.test/club.png",
                UserId = owner.Id
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync();

            return new EventsRepositoryHarness(connection, db, club);
        }

        public EventEntity BuildEvent(
            string name,
            DateTime startTime,
            DateTime? endTime = null,
            string city = "Ottawa",
            EventCategory category = EventCategory.Social,
            int registrationCount = 0,
            double? latitude = 45.4215,
            double? longitude = -75.6972,
            List<EventImage>? images = null)
        {
            return new EventEntity
            {
                Name = name,
                Description = $"{name} description",
                Location = "Campus Hall",
                ClubId = Club.Id,
                StartTime = startTime,
                EndTime = endTime,
                LifecycleState = EventLifecycleState.Published,
                Category = category,
                City = city,
                Latitude = latitude,
                Longitude = longitude,
                Tags = ["tag"],
                RegistrationCount = registrationCount,
                Images = images ?? [new EventImage { ImageUrl = $"https://cdn.test/{name}.png", SortOrder = 0 }]
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
