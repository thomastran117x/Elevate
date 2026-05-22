using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.reviews;
using backend.main.features.clubs.search;
using backend.main.features.clubs.staff;
using backend.main.features.events;
using backend.main.features.events.images;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.payment;
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
    public async Task SeedUsersSeeder_ShouldCreateFortyTwoUsers_AndRemainIdempotent()
    {
        await using var db = await CreateDbContextAsync();
        var seeder = CreateUsersSeeder(db);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var users = await db.Users
            .OrderBy(user => user.Email)
            .ToListAsync();

        users.Should().HaveCount(42);
        users.Select(user => user.Email).Should().OnlyHaveUniqueItems();
        users.Count(user => user.Usertype == "Organizer").Should().Be(20);
        users.Count(user => user.Usertype == "Volunteer").Should().Be(10);
        users.Count(user => user.Usertype == "Participant").Should().Be(12);
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
    public async Task ThematicSeeders_ShouldCreateOneHundredClubPosts_WithExpectedPerClubCounts()
    {
        await using var db = await CreateDbContextAsync();

        await RunSeedersAsync(db);

        var clubs = await db.Clubs
            .OrderBy(club => club.Name)
            .ToListAsync();
        var posts = await db.ClubPosts
            .OrderBy(post => post.ClubId)
            .ThenBy(post => post.CreatedAt)
            .ToListAsync();

        posts.Should().HaveCount(100);

        foreach (var definition in ClubSources.Select(source => source.Definition))
        {
            var club = clubs.Single(entry => entry.Name == definition.Name);
            var clubPosts = posts.Where(post => post.ClubId == club.Id).ToList();

            clubPosts.Should().HaveCount(10);
            clubPosts.Count(post => post.IsPinned).Should().BeGreaterThanOrEqualTo(2);
            clubPosts.Should().Contain(post => post.PostType == PostType.Announcement);
            clubPosts.Should().Contain(post => post.PostType == PostType.General);
        }
    }

    [Fact]
    public async Task ThematicSeeders_ShouldPopulateFeatureActivityAcrossSeededEntities()
    {
        await using var db = await CreateDbContextAsync();

        await RunSeedersAsync(db);

        var follows = await db.FollowClubs.ToListAsync();
        var reviews = await db.ClubReviews.ToListAsync();
        var comments = await db.PostComments.ToListAsync();
        var images = await db.EventImages.OrderBy(image => image.EventId).ThenBy(image => image.SortOrder).ToListAsync();
        var registrations = await db.EventRegistrations.ToListAsync();
        var payments = await db.Payments.ToListAsync();
        var invitationLinks = await db.EventInvitationLinks.ToListAsync();
        var invitations = await db.EventInvitations.ToListAsync();
        var clubs = await db.Clubs.OrderBy(club => club.Name).ToListAsync();
        var events = await db.Events.OrderBy(ev => ev.ClubId).ThenBy(ev => ev.Name).ToListAsync();
        var clubVersions = await db.ClubVersions.ToListAsync();
        var eventVersions = await db.EventVersions.ToListAsync();

        follows.Should().HaveCount(40);
        reviews.Should().HaveCount(30);
        comments.Should().HaveCount(40);
        images.Should().HaveCount(30);
        registrations.Should().HaveCount(60);
        payments.Should().HaveCount(30);
        invitationLinks.Should().HaveCount(10);
        invitations.Should().HaveCount(60);
        clubVersions.Should().HaveCount(20);
        eventVersions.Should().HaveCount(570);

        clubs.Should().OnlyContain(club => club.MemberCount == 4);
        clubs.Should().OnlyContain(club => club.Rating == 4.7);
        events.Where(ev => ev.RegistrationCount > 0).Should().HaveCount(30);
        events.Sum(ev => ev.RegistrationCount).Should().Be(60);
        payments.Count(payment => payment.Status == PaymentStatus.Succeeded).Should().Be(10);
        payments.Count(payment => payment.Status == PaymentStatus.Pending).Should().Be(10);
        payments.Count(payment => payment.Status == PaymentStatus.Refunded).Should().Be(10);
        invitations.Count(invitation => invitation.SourceType == EventInvitationSource.LinkClaim).Should().Be(20);
        invitations.Count(invitation => invitation.LifecycleStatus == EventInvitationLifecycleStatus.Accepted).Should().Be(20);
        invitations.Count(invitation => invitation.LifecycleStatus == EventInvitationLifecycleStatus.Declined).Should().Be(20);
        invitations.Count(invitation => invitation.LifecycleStatus == EventInvitationLifecycleStatus.Pending).Should().Be(10);
        invitations.Count(invitation => invitation.LifecycleStatus == EventInvitationLifecycleStatus.Revoked).Should().Be(10);
        invitationLinks.Should().OnlyContain(link => link.RedemptionCount == 1);
        images.GroupBy(image => image.EventId).Should().OnlyContain(group => group.Select(image => image.SortOrder).SequenceEqual(Enumerable.Range(0, group.Count())));
        clubs.Should().OnlyContain(club => club.CurrentVersionNumber == 2);
        events.Count(ev => ev.CurrentVersionNumber == 2).Should().Be(20);
        events.Count(ev => ev.CurrentVersionNumber == 1).Should().Be(530);
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
        var originalManagedPost = await db.ClubPosts
            .Where(post => post.ClubId == harbourClub.Id)
            .OrderBy(post => post.Title)
            .FirstAsync();
        var outsider = new User
        {
            Email = "outsider@example.com",
            Username = "outsider",
            Name = "Outsider User",
            Usertype = "Participant",
            Password = "placeholder",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(outsider);
        await db.SaveChangesAsync();

        var paidEvent = await db.Events
            .Where(ev => ev.ClubId == harbourClub.Id && !ev.isPrivate && ev.registerCost > 0)
            .OrderBy(ev => ev.StartTime)
            .FirstAsync();

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

        db.ClubPosts.Add(new ClubPost
        {
            ClubId = harbourClub.Id,
            UserId = harbourClub.UserId,
            Title = "Legacy Seed Post",
            Content = "Legacy seeded post that should be removed.",
            PostType = PostType.General,
            LikesCount = 1,
            ViewCount = 9,
            IsPinned = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        db.FollowClubs.Add(new FollowClub
        {
            ClubId = harbourClub.Id,
            UserId = outsider.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.ClubReviews.Add(new ClubReview
        {
            ClubId = harbourClub.Id,
            UserId = outsider.Id,
            Title = "Outsider review",
            Rating = 5,
            Comment = "Should remain after rerun.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.PostComments.Add(new PostComment
        {
            PostId = originalManagedPost.Id,
            UserId = outsider.Id,
            Content = "Outsider comment should remain.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = paidEvent.Id,
            UserId = outsider.Id,
            CreatedAt = DateTime.UtcNow
        });
        db.Payments.Add(new Payment
        {
            UserId = outsider.Id,
            EventId = paidEvent.Id,
            Amount = paidEvent.registerCost,
            Currency = "usd",
            Status = PaymentStatus.Succeeded,
            IdempotencyKey = "outsider-payment",
            ExternalSessionId = "outsider-session",
            ExternalPaymentIntentId = "outsider-intent",
            CheckoutUrl = "https://checkout.example.com/outsider",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        originalManagedEvent.Description = "Drifted description";
        originalManagedPost.Content = "Drifted post content";

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
        var activitySeeder = CreateActivitySeeder(db);
        await clubSeeder.SeedAsync();
        await activitySeeder.SeedAsync();

        var refreshedHarbourEvents = await db.Events
            .Where(ev => ev.ClubId == harbourClub.Id)
            .ToListAsync();
        var refreshedHarbourPosts = await db.ClubPosts
            .Where(post => post.ClubId == harbourClub.Id)
            .ToListAsync();
        var refreshedHarbourStaff = await db.ClubStaff
            .Where(entry => entry.ClubId == harbourClub.Id)
            .ToListAsync();
        var refreshedOriginalEvent = refreshedHarbourEvents.Single(ev => ev.Id == originalManagedEvent.Id);
        var refreshedOriginalPost = refreshedHarbourPosts.Single(post => post.Id == originalManagedPost.Id);

        refreshedHarbourEvents.Should().HaveCount(55);
        refreshedHarbourPosts.Should().HaveCount(10);
        refreshedHarbourEvents.Should().NotContain(ev => ev.Name == "Legacy Seed Event 99");
        refreshedHarbourPosts.Should().NotContain(post => post.Title == "Legacy Seed Post");
        refreshedOriginalEvent.Description.Should().NotBe("Drifted description");
        refreshedOriginalPost.Content.Should().NotBe("Drifted post content");
        refreshedHarbourStaff.Should().HaveCount(2);
        refreshedHarbourStaff.Should().NotContain(entry => entry.UserId == extraStaffUser.Id);
        (await db.FollowClubs.AnyAsync(follow => follow.UserId == outsider.Id && follow.ClubId == harbourClub.Id)).Should().BeTrue();
        (await db.ClubReviews.AnyAsync(review => review.UserId == outsider.Id && review.ClubId == harbourClub.Id)).Should().BeTrue();
        (await db.PostComments.AnyAsync(comment => comment.UserId == outsider.Id && comment.PostId == originalManagedPost.Id)).Should().BeTrue();
        (await db.EventRegistrations.AnyAsync(registration => registration.UserId == outsider.Id && registration.EventId == paidEvent.Id)).Should().BeTrue();
        (await db.Payments.AnyAsync(payment => payment.UserId == outsider.Id && payment.EventId == paidEvent.Id)).Should().BeTrue();
        (await db.Clubs.AnyAsync(club => club.Name == "Legacy Seed Club")).Should().BeFalse();
        (await db.Users.AnyAsync(user => user.Email == legacyUser.Email)).Should().BeFalse();
    }

    private static async Task RunSeedersAsync(AppDatabaseContext db)
    {
        var usersSeeder = CreateUsersSeeder(db);
        var clubSeeder = CreateClubSeeder(db);
        var activitySeeder = CreateActivitySeeder(db);

        await usersSeeder.SeedAsync();
        await clubSeeder.SeedAsync();
        await activitySeeder.SeedAsync();
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
            new ClubPostSearchOutboxWriter(db),
            new ClubSearchOutboxWriter(db),
            ClubSources,
            NullLogger<SeedClubContentSeeder>.Instance);
    }

    private static SeedFeatureActivitySeeder CreateActivitySeeder(AppDatabaseContext db)
    {
        return new SeedFeatureActivitySeeder(
            db,
            new EventSearchOutboxWriter(db),
            new ClubSearchOutboxWriter(db),
            ClubSources,
            NullLogger<SeedFeatureActivitySeeder>.Instance);
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
