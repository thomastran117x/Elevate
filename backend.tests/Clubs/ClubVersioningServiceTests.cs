using System.Security.Claims;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.search;
using backend.main.features.clubs.versions;
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

namespace backend.tests.Clubs;

public class ClubVersioningServiceTests
{
    [Fact]
    public async Task CreateClub_ShouldCreateInitialVersionWithActorMetadata()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var club = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        club.CurrentVersionNumber.Should().Be(1);

        var version = await harness.Db.ClubVersions.SingleAsync();
        var snapshot = JsonSerializer.Deserialize<ClubVersionSnapshot>(version.SnapshotJson);
        var changes = JsonSerializer.Deserialize<List<ClubVersionFieldChange>>(version.ChangedFieldsJson);

        version.VersionNumber.Should().Be(1);
        version.ActionType.Should().Be(ClubVersionActions.Create);
        version.ActorUserId.Should().Be(7);
        version.ActorRole.Should().Be("Organizer");
        snapshot.Should().NotBeNull();
        snapshot!.Name.Should().Be("Chess Club");
        changes.Should().Contain(change => change.Field == "name" && change.NewValue == "Chess Club");
    }

    [Fact]
    public async Task UpdateClub_ShouldCreateVersionAndPreserveOwnerDuringAdminEdit()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        var updated = await harness.Service.UpdateClub(
            created.Id,
            userId: 99,
            userRole: "Admin",
            name: "Campus Chess Club",
            description: "Competitive ladder and casual boards",
            clubtype: "Social",
            clubimage: CreateFormFile("club-v2.png"),
            phone: "555-111-2222",
            email: "club@test.local");

        updated.UserId.Should().Be(7);
        updated.CurrentVersionNumber.Should().Be(2);

        var versions = await harness.Db.ClubVersions
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();

        versions.Should().HaveCount(2);
        versions[1].ActionType.Should().Be(ClubVersionActions.Update);
        versions[1].ActorUserId.Should().Be(99);
        versions[1].ActorRole.Should().Be("Admin");
        JsonSerializer.Deserialize<List<ClubVersionFieldChange>>(versions[1].ChangedFieldsJson)!
            .Should()
            .Contain(change => change.Field == "name" && change.NewValue == "Campus Chess Club");
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldRestoreVersionedFieldsOnly_AndCreateRollbackVersion()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        harness.TimeProvider.Advance(TimeSpan.FromDays(5));

        await harness.Service.UpdateClub(
            created.Id,
            userId: 7,
            userRole: "Organizer",
            name: "Campus Chess Club",
            description: "Competitive ladder and casual boards",
            clubtype: "Social",
            clubimage: CreateFormFile("club-v2.png"),
            phone: "555-111-2222",
            email: "club@test.local");

        var liveClub = await harness.Db.Clubs.SingleAsync();
        liveClub.MemberCount = 17;
        liveClub.EventCount = 4;
        liveClub.AvaliableEventCount = 2;
        liveClub.Rating = 4.7;
        await harness.Db.SaveChangesAsync();

        var rollback = await harness.Service.RollbackToVersionAsync(
            created.Id,
            versionNumber: 1,
            userId: 7,
            userRole: "Organizer");

        rollback.NewVersionNumber.Should().Be(3);

        var reloaded = await harness.Db.Clubs.SingleAsync();
        reloaded.Name.Should().Be("Chess Club");
        reloaded.Description.Should().Be("Weekly strategy nights");
        reloaded.ClubImage.Should().Be("https://cdn.test/clubs/club-v1.png");
        reloaded.MemberCount.Should().Be(17);
        reloaded.EventCount.Should().Be(4);
        reloaded.AvaliableEventCount.Should().Be(2);
        reloaded.Rating.Should().Be(4.7);
        reloaded.CurrentVersionNumber.Should().Be(3);

        var latestVersion = await harness.Db.ClubVersions
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync();

        latestVersion.ActionType.Should().Be(ClubVersionActions.Rollback);
        latestVersion.RollbackSourceVersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task RollbackToVersionAsync_ShouldRejectExpiredVersion()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        harness.TimeProvider.Advance(TimeSpan.FromDays(91));

        await harness.Service.UpdateClub(
            created.Id,
            userId: 7,
            userRole: "Organizer",
            name: "Campus Chess Club",
            description: "Competitive ladder and casual boards",
            clubtype: "Social",
            clubimage: CreateFormFile("club-v2.png"));

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
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        var act = () => harness.Service.GetVersionHistoryAsync(
            created.Id,
            userId: 55,
            userRole: "Participant");

        await act.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task GetManagedClubsAsync_ShouldReturnAllOwnedAndManagedClubs()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var first = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        var second = await harness.Service.CreateClub(
            "Robotics Club",
            7,
            "Building and competing together.",
            "Academic",
            CreateFormFile("club-v2.png"));

        await harness.Service.AddStaffAsync(first.Id, 55, backend.main.features.clubs.staff.ClubStaffRole.Manager, 7, "Organizer");

        var ownedClubs = await harness.Service.GetManagedClubsAsync(7);
        var managedClubs = await harness.Service.GetManagedClubsAsync(55);

        ownedClubs.Select(club => club.Id).Should().BeEquivalentTo([first.Id, second.Id]);
        managedClubs.Select(club => club.Id).Should().BeEquivalentTo([first.Id]);
    }

    [Fact]
    public async Task AddManagerAsync_ShouldRejectDuplicateManagerAssignments()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        var manager = await harness.Service.AddStaffAsync(created.Id, 55, backend.main.features.clubs.staff.ClubStaffRole.Manager, 7, "Organizer");

        manager.UserId.Should().Be(55);
        manager.Role.Should().Be(backend.main.features.clubs.staff.ClubStaffRole.Manager);

        var act = () => harness.Service.AddStaffAsync(created.Id, 55, backend.main.features.clubs.staff.ClubStaffRole.Manager, 7, "Organizer");

        await act.Should()
            .ThrowAsync<ConflictException>()
            .WithMessage("User already has a staff role for this club.");
    }

    [Fact]
    public async Task UpdateClub_ShouldAllowManager()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        await harness.Service.AddStaffAsync(created.Id, 55, backend.main.features.clubs.staff.ClubStaffRole.Manager, 7, "Organizer");

        var updated = await harness.Service.UpdateClub(
            created.Id,
            userId: 55,
            userRole: "Participant",
            name: "Campus Chess Club",
            description: "Manager-updated description",
            clubtype: "Social",
            clubimage: CreateFormFile("club-v2.png"));

        updated.Name.Should().Be("Campus Chess Club");
        updated.UserId.Should().Be(7);

        var latestVersion = await harness.Db.ClubVersions
            .OrderByDescending(version => version.VersionNumber)
            .FirstAsync();

        latestVersion.ActorUserId.Should().Be(55);
        latestVersion.ActorRole.Should().Be("Participant");
    }

    [Fact]
    public async Task DeleteClub_ShouldRejectManager()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        await harness.Service.AddStaffAsync(created.Id, 55, backend.main.features.clubs.staff.ClubStaffRole.Manager, 7, "Organizer");

        var act = () => harness.Service.DeleteClub(created.Id, 55);

        await act.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task UpdateClub_ShouldRejectVolunteer()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        await harness.Service.AddStaffAsync(
            created.Id,
            55,
            backend.main.features.clubs.staff.ClubStaffRole.Volunteer,
            7,
            "Organizer");

        var act = () => harness.Service.UpdateClub(
            created.Id,
            userId: 55,
            userRole: "Participant",
            name: "Volunteer Edit",
            description: "Should not be allowed",
            clubtype: "Social",
            clubimage: CreateFormFile("club-v2.png"));

        await act.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task TransferOwnershipAsync_ShouldMoveOwnerAndRemoveManagerRoleFromNewOwner()
    {
        await using var harness = await ClubServiceHarness.CreateAsync();

        var created = await harness.Service.CreateClub(
            "Chess Club",
            7,
            "Weekly strategy nights",
            "Social",
            CreateFormFile("club-v1.png"));

        await harness.Service.AddStaffAsync(created.Id, 55, backend.main.features.clubs.staff.ClubStaffRole.Manager, 7, "Organizer");

        var transferred = await harness.Service.TransferOwnershipAsync(created.Id, 55, 7, "Organizer");

        transferred.UserId.Should().Be(55);
        (await harness.Db.ClubStaff.AnyAsync(staff => staff.ClubId == created.Id && staff.UserId == 55))
            .Should()
            .BeFalse();

        var act = () => harness.Service.UpdateClub(
            created.Id,
            userId: 7,
            userRole: "Organizer",
            name: "Former Owner Edit",
            description: "Should not be allowed",
            clubtype: "Social",
            clubimage: CreateFormFile("club-v3.png"));

        await act.Should()
            .ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed");
    }

    [Fact]
    public async Task CleanupRunner_ShouldDeleteOnlyImagesThatAreNoLongerRollbackableOrCurrent()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDatabaseContext(dbOptions);
        await db.Database.EnsureCreatedAsync();

        var now = new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);

        db.Users.Add(new User
        {
            Id = 7,
            Email = "owner@test.local",
            Usertype = "Organizer"
        });

        db.Clubs.Add(new Club
        {
            Id = 1,
            UserId = 7,
            Name = "Chess Club",
            Description = "Weekly strategy nights",
            Clubtype = ClubType.Social,
            ClubImage = "https://cdn.test/clubs/current.png",
            CurrentVersionNumber = 3,
            CreatedAt = now.UtcDateTime.AddDays(-120),
            UpdatedAt = now.UtcDateTime,
        });

        db.ClubVersions.AddRange(
            new ClubVersion
            {
                ClubId = 1,
                VersionNumber = 1,
                ActionType = ClubVersionActions.Create,
                SnapshotJson = "{}",
                ChangedFieldsJson = "[]",
                ClubImage = "https://cdn.test/clubs/delete-me.png",
                ActorUserId = 7,
                ActorRole = "Organizer",
                CreatedAt = now.UtcDateTime.AddDays(-120),
            },
            new ClubVersion
            {
                ClubId = 1,
                VersionNumber = 2,
                ActionType = ClubVersionActions.Update,
                SnapshotJson = "{}",
                ChangedFieldsJson = "[]",
                ClubImage = "https://cdn.test/clubs/protected.png",
                ActorUserId = 7,
                ActorRole = "Organizer",
                CreatedAt = now.UtcDateTime.AddDays(-120),
            },
            new ClubVersion
            {
                ClubId = 1,
                VersionNumber = 3,
                ActionType = ClubVersionActions.Update,
                SnapshotJson = "{}",
                ChangedFieldsJson = "[]",
                ClubImage = "https://cdn.test/clubs/protected.png",
                ActorUserId = 7,
                ActorRole = "Organizer",
                CreatedAt = now.UtcDateTime.AddDays(-10),
            },
            new ClubVersion
            {
                ClubId = 1,
                VersionNumber = 4,
                ActionType = ClubVersionActions.Update,
                SnapshotJson = "{}",
                ChangedFieldsJson = "[]",
                ClubImage = "https://cdn.test/clubs/current.png",
                ActorUserId = 7,
                ActorRole = "Organizer",
                CreatedAt = now.UtcDateTime.AddDays(-120),
            });

        await db.SaveChangesAsync();

        var fileUpload = new Mock<IFileUploadService>();
        var deletedUrls = new List<string>();
        fileUpload
            .Setup(service => service.DeleteImageAsync(It.IsAny<string>()))
            .Returns<string>(url =>
            {
                deletedUrls.Add(url);
                return Task.CompletedTask;
            });

        var runner = new ClubVersionCleanupRunner(
            db,
            fileUpload.Object,
            Options.Create(new ClubVersioningOptions
            {
                RollbackWindowDays = 90,
                PurgeBatchSize = 20,
                PurgeEnabled = true
            }),
            timeProvider);

        await runner.RunOnceAsync();

        deletedUrls.Should().ContainSingle()
            .Which.Should().Be("https://cdn.test/clubs/delete-me.png");

        await connection.DisposeAsync();
    }

    private static FormFile CreateFormFile(string fileName)
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "clubImage", fileName);
    }

    private sealed class ClubServiceHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public ClubService Service { get; }
        public TestTimeProvider TimeProvider { get; }

        private ClubServiceHarness(
            SqliteConnection connection,
            AppDatabaseContext db,
            ClubService service,
            TestTimeProvider timeProvider)
        {
            _connection = connection;
            Db = db;
            Service = service;
            TimeProvider = timeProvider;
        }

        public static async Task<ClubServiceHarness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var dbOptions = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(dbOptions);
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(
                new User
                {
                    Id = 7,
                    Email = "owner@test.local",
                    Usertype = "Organizer"
                },
                new User
                {
                    Id = 55,
                    Email = "manager@test.local",
                    Usertype = "Participant"
                },
                new User
                {
                    Id = 99,
                    Email = "admin@test.local",
                    Usertype = "Admin"
                });
            await db.SaveChangesAsync();

            var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 5, 13, 12, 0, 0, TimeSpan.Zero));

            var cache = new Mock<ICacheService>();
            cache.Setup(service => service.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
                .ReturnsAsync(true);
            cache.Setup(service => service.GetValueAsync(It.IsAny<string>()))
                .ReturnsAsync((string?)null);
            cache.Setup(service => service.IncrementAsync(It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(1L);
            cache.Setup(service => service.GetManyAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new Dictionary<string, string?>());
            cache.Setup(service => service.DeleteKeyAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
            cache.Setup(service => service.KeyExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            cache.Setup(service => service.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);

            var fileUpload = new Mock<IFileUploadService>();
            fileUpload.SetupSequence(service => service.UploadImageAsync(It.IsAny<IFormFile>(), "clubs"))
                .ReturnsAsync("https://cdn.test/clubs/club-v1.png")
                .ReturnsAsync("https://cdn.test/clubs/club-v2.png")
                .ReturnsAsync("https://cdn.test/clubs/club-v3.png");
            fileUpload.Setup(service => service.DeleteImageAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var userService = new Mock<IUserService>();
            userService.Setup(service => service.GetUserByIdAsync(7))
                .ReturnsAsync(new User
                {
                    Id = 7,
                    Email = "owner@test.local",
                    Usertype = "Organizer"
                });
            userService.Setup(service => service.GetUserByIdAsync(55))
                .ReturnsAsync(new User
                {
                    Id = 55,
                    Email = "manager@test.local",
                    Usertype = "Participant"
                });
            userService.Setup(service => service.GetUserByIdAsync(99))
                .ReturnsAsync(new User
                {
                    Id = 99,
                    Email = "admin@test.local",
                    Usertype = "Admin"
                });

            var followService = new Mock<IFollowService>();
            var searchService = new Mock<IClubSearchService>();
            searchService
                .Setup(service => service.SearchAsync(It.IsAny<ClubSearchCriteria>()))
                .ReturnsAsync(new ClubSearchResult(new List<ClubSearchHit>(), 0));

            var outboxWriter = new Mock<IClubSearchOutboxWriter>();

            var service = new ClubService(
                db,
                new ClubRepository(db),
                userService.Object,
                fileUpload.Object,
                followService.Object,
                cache.Object,
                searchService.Object,
                outboxWriter.Object,
                Options.Create(new ClubVersioningOptions
                {
                    RollbackWindowDays = 90,
                    PurgeBatchSize = 20,
                    PurgeEnabled = true
                }),
                timeProvider);

            return new ClubServiceHarness(connection, db, service, timeProvider);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
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
