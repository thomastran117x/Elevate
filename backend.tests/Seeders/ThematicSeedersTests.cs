using backend.main.features.clubs;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.search;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.seeders;
using backend.main.seeders.clubs;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace backend.tests.Seeders;

public class ThematicSeedersTests
{
    [Fact]
    public async Task SeedUsersSeeder_ShouldCreateExactThirtyUsers_AndRemainIdempotent()
    {
        await using var db = await CreateDbContextAsync();
        var seeder = CreateUsersSeeder(db);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var users = await db.Users
            .OrderBy(user => user.Email)
            .ToListAsync();

        users.Should().HaveCount(30);
        users.Select(user => user.Email).Should().OnlyHaveUniqueItems();
        users.Count(user => user.Usertype == "Organizer").Should().Be(20);
        users.Count(user => user.Usertype == "Volunteer").Should().Be(10);
    }

    [Fact]
    public async Task ThematicSeeders_ShouldCreateTenClubs_WithExactStaffAssignments()
    {
        await using var db = await CreateDbContextAsync();

        await RunSeedersAsync(db);

        var clubs = await db.Clubs
            .OrderBy(club => club.Name)
            .ToListAsync();
        var staff = await db.ClubStaff
            .OrderBy(entry => entry.ClubId)
            .ThenBy(entry => entry.Role)
            .ToListAsync();

        clubs.Should().HaveCount(10);
        staff.Should().HaveCount(20);

        foreach (var definition in ClubSources.Select(source => source.Definition))
        {
            var club = clubs.Single(entry => entry.Name == definition.Name);
            var clubStaff = staff.Where(entry => entry.ClubId == club.Id).ToList();

            clubStaff.Should().HaveCount(2);
            clubStaff.Count(entry => entry.Role == ClubStaffRole.Manager).Should().Be(1);
            clubStaff.Count(entry => entry.Role == ClubStaffRole.Volunteer).Should().Be(1);

            var owner = await db.Users.SingleAsync(user => user.Id == club.UserId);
            owner.Email.Should().Be(definition.OwnerEmail);
        }
    }

    [Fact]
    public async Task ThematicSeeders_ShouldCreateFiveHundredFiftyEvents_WithExpectedPerClubCounts()
    {
        await using var db = await CreateDbContextAsync();

        await RunSeedersAsync(db);

        var clubs = await db.Clubs
            .OrderBy(club => club.Name)
            .ToListAsync();
        var events = await db.Events
            .OrderBy(ev => ev.ClubId)
            .ThenBy(ev => ev.Name)
            .ToListAsync();

        events.Should().HaveCount(550);

        foreach (var definition in ClubSources.Select(source => source.Definition))
        {
            var club = clubs.Single(entry => entry.Name == definition.Name);
            var clubEvents = events.Where(ev => ev.ClubId == club.Id).ToList();

            clubEvents.Should().HaveCount(55);
            clubEvents.Count(ev => !ev.isPrivate).Should().Be(50);
            clubEvents.Count(ev => ev.isPrivate).Should().Be(5);
            club.EventCount.Should().Be(55);
            club.AvaliableEventCount.Should().Be(55);
            clubEvents.Should().OnlyContain(ev => ev.Tags.Contains(SeedCatalogConstants.SeedEventTag));
            clubEvents.Should().OnlyContain(ev => ev.Tags.Contains(SeedCatalogConstants.ClubSeedTag(definition.Slug)));
        }
    }

    [Fact]
    public async Task ThematicSeeders_ShouldPruneStaleSeedContent_AndRestoreDriftOnRerun()
    {
        await using var db = await CreateDbContextAsync();

        await RunSeedersAsync(db);

        var harbourDefinition = ClubSources
            .Select(source => source.Definition)
            .Single(definition => definition.Slug == "harbour-striders");
        var harbourClub = await db.Clubs.SingleAsync(club => club.Name == harbourDefinition.Name);
        var originalManagedEvent = await db.Events
            .Where(ev => ev.ClubId == harbourClub.Id)
            .OrderBy(ev => ev.Name)
            .FirstAsync();
        var extraStaffUser = await db.Users.SingleAsync(user => user.Email == $"builders.owner{SeedCatalogConstants.SeedEmailDomain}");

        db.ClubStaff.Add(new ClubStaff
        {
            ClubId = harbourClub.Id,
            UserId = extraStaffUser.Id,
            Role = ClubStaffRole.Volunteer,
            GrantedByUserId = harbourClub.UserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.Events.Add(new backend.main.features.events.Events
        {
            Name = "Legacy Seed Event 99",
            Description = "Legacy seeded content that should be removed.",
            Location = "Queens Quay Trailhead",
            isPrivate = false,
            maxParticipants = 12,
            registerCost = 0,
            StartTime = DateTime.UtcNow.AddDays(30),
            EndTime = DateTime.UtcNow.AddDays(30).AddHours(2),
            ClubId = harbourClub.Id,
            CurrentVersionNumber = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Category = EventCategory.Fitness,
            VenueName = "Harbourfront Trailhead",
            City = "Toronto",
            Latitude = 43.6387,
            Longitude = -79.3817,
            Tags =
            [
                "running",
                SeedCatalogConstants.SeedEventTag,
                SeedCatalogConstants.ClubSeedTag(harbourDefinition.Slug)
            ]
        });

        originalManagedEvent.Description = "Drifted description";

        var legacyUser = new User
        {
            Email = $"legacy.orphan{SeedCatalogConstants.SeedEmailDomain}",
            Username = "legacyorphan",
            Name = "Legacy Orphan",
            Usertype = "Organizer",
            Password = "placeholder",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(legacyUser);
        await db.SaveChangesAsync();

        db.Clubs.Add(new Club
        {
            Name = "Legacy Seed Club",
            Description = "Legacy seeded club.",
            Clubtype = ClubType.Other,
            ClubImage = "https://placehold.co/1200x800?text=Legacy+Seed+Club",
            Email = $"legacy.club{SeedCatalogConstants.SeedEmailDomain}",
            WebsiteUrl = $"https://{SeedCatalogConstants.SeedWebsiteHost}/clubs/legacy-seed-club",
            Location = "Legacy Hall",
            MaxMemberCount = 50,
            UserId = legacyUser.Id,
            CurrentVersionNumber = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var legacyClub = await db.Clubs.SingleAsync(club => club.Name == "Legacy Seed Club");
        db.Events.Add(new backend.main.features.events.Events
        {
            Name = "Legacy Club Event",
            Description = "Legacy club event that should be deleted with the club.",
            Location = "Legacy Hall",
            isPrivate = false,
            maxParticipants = 20,
            registerCost = 0,
            StartTime = DateTime.UtcNow.AddDays(10),
            EndTime = DateTime.UtcNow.AddDays(10).AddHours(2),
            ClubId = legacyClub.Id,
            CurrentVersionNumber = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Category = EventCategory.Other,
            VenueName = "Legacy Hall",
            City = "Toronto",
            Latitude = 43.6500,
            Longitude = -79.3800,
            Tags = ["legacy"]
        });
        await db.SaveChangesAsync();

        var clubSeeder = CreateClubSeeder(db);
        await clubSeeder.SeedAsync();

        var refreshedHarbourEvents = await db.Events
            .Where(ev => ev.ClubId == harbourClub.Id)
            .ToListAsync();
        var refreshedHarbourStaff = await db.ClubStaff
            .Where(entry => entry.ClubId == harbourClub.Id)
            .ToListAsync();
        var refreshedOriginalEvent = refreshedHarbourEvents.Single(ev => ev.Id == originalManagedEvent.Id);

        refreshedHarbourEvents.Should().HaveCount(55);
        refreshedHarbourEvents.Should().NotContain(ev => ev.Name == "Legacy Seed Event 99");
        refreshedOriginalEvent.Description.Should().NotBe("Drifted description");
        refreshedHarbourStaff.Should().HaveCount(2);
        refreshedHarbourStaff.Should().NotContain(entry => entry.UserId == extraStaffUser.Id);
        (await db.Clubs.AnyAsync(club => club.Name == "Legacy Seed Club")).Should().BeFalse();
        (await db.Users.AnyAsync(user => user.Email == legacyUser.Email)).Should().BeFalse();
    }

    private static async Task RunSeedersAsync(AppDatabaseContext db)
    {
        var usersSeeder = CreateUsersSeeder(db);
        var clubSeeder = CreateClubSeeder(db);

        await usersSeeder.SeedAsync();
        await clubSeeder.SeedAsync();
    }

    private static SeedUsersSeeder CreateUsersSeeder(AppDatabaseContext db)
    {
        return new SeedUsersSeeder(
            db,
            BuildConfiguration(),
            NullLogger<SeedUsersSeeder>.Instance);
    }

    private static SeedClubContentSeeder CreateClubSeeder(AppDatabaseContext db)
    {
        return new SeedClubContentSeeder(
            db,
            new EventSearchOutboxWriter(db),
            new ClubSearchOutboxWriter(db),
            ClubSources,
            NullLogger<SeedClubContentSeeder>.Instance);
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AUTH_SEED_USERS_PASSWORD"] = "Password123!"
            })
            .Build();
    }

    private static async Task<AppDatabaseContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var context = new AppDatabaseContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static readonly IReadOnlyList<IClubSeedDefinitionSource> ClubSources =
    [
        new HarbourStridersClubSeed(),
        new SummitTrailSocietyClubSeed(),
        new NorthCampusBuildersClubSeed(),
        new CivicSpeakersForumClubSeed(),
        new LanternSocialCollectiveClubSeed(),
        new WeekendMakersCommonsClubSeed(),
        new MosaicArtsCircleClubSeed(),
        new RhythmExchangeCollectiveClubSeed(),
        new PixelPlayGuildClubSeed(),
        new NeighbourhoodKitchenTableClubSeed()
    ];
}
