using backend.main.features.auth;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace backend.tests.Unit.Features.Auth;

public class AuthUserRepositoryTests
{
    [Fact]
    public async Task CreateUserAsync_ShouldPersistUser_AndNormalizeRole()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();

        var created = await harness.Repository.CreateUserAsync(new User
        {
            Email = "new@example.com",
            Password = "hashed-password",
            Usertype = "organizer",
            Name = "New User"
        });

        created.Id.Should().BeGreaterThan(0);
        created.Usertype.Should().Be("Organizer");

        var stored = await harness.Db.Users.SingleAsync(user => user.Email == "new@example.com");
        stored.Usertype.Should().Be("Organizer");
        stored.Name.Should().Be("New User");
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldUpdateKnownFields_AndNormalizeRole()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var updated = await harness.Repository.UpdateUserAsync(userId, new User
        {
            Email = "ignored@example.com",
            Password = "new-hash",
            Usertype = "admin",
            Name = "Updated Name",
            Username = "updated-user",
            Avatar = "/avatars/updated.png",
            Address = "123 Updated Street",
            Phone = "555-0100"
        });

        updated.Should().NotBeNull();
        updated!.Password.Should().Be("new-hash");
        updated.Usertype.Should().Be("Admin");
        updated.Name.Should().Be("Updated Name");
        updated.Username.Should().Be("updated-user");
        updated.Avatar.Should().Be("/avatars/updated.png");
        updated.Address.Should().Be("123 Updated Street");
        updated.Phone.Should().Be("555-0100");
        updated.Email.Should().Be("seed@example.com");
    }

    [Fact]
    public async Task UpdatePartialAsync_ShouldChangeMutableFields_ButPreserveIdentityAndRole()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var updated = await harness.Repository.UpdatePartialAsync(new User
        {
            Id = userId,
            Email = "partial@example.com",
            Usertype = "organizer",
            Name = "Partial Name"
        });

        updated.Should().NotBeNull();
        // Identity and role are intentionally NOT mutable via UpdatePartialAsync: even when
        // Email/Usertype are supplied they must be ignored, so a stale JWT claim can never
        // silently overwrite them. Email changes require re-verification and role changes go
        // through dedicated admin/status flows.
        updated!.Email.Should().Be("seed@example.com");
        updated.Usertype.Should().Be("participant");
        // Mutable profile fields are still applied.
        updated.Name.Should().Be("Partial Name");
        updated.Username.Should().Be("seed-user");
        updated.Password.Should().Be("seed-password");
    }

    [Fact]
    public async Task UpdateProviderIdsAsync_ShouldUpdateProviderValues_AndReturnOAuthRecord()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var updated = await harness.Repository.UpdateProviderIdsAsync(userId, "google-123", "ms-456");

        updated.Should().NotBeNull();
        updated!.GoogleID.Should().Be("google-123");
        updated.MicrosoftID.Should().Be("ms-456");
        updated.Usertype.Should().Be("Participant");
    }

    [Fact]
    public async Task UpdateUserStatusAsync_ShouldToggleDisabledState_ClearReasonOnEnable_AndIncrementAuthVersion()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var disabled = await harness.Repository.UpdateUserStatusAsync(userId, true, "policy");
        disabled.Should().NotBeNull();
        disabled!.IsDisabled.Should().BeTrue();
        disabled.DisabledReason.Should().Be("policy");
        disabled.DisabledAtUtc.Should().NotBeNull();
        disabled.AuthVersion.Should().Be(2);

        var enabled = await harness.Repository.UpdateUserStatusAsync(userId, false, "ignored");
        enabled.Should().NotBeNull();
        enabled!.IsDisabled.Should().BeFalse();
        enabled.DisabledReason.Should().BeNull();
        enabled.DisabledAtUtc.Should().BeNull();
        enabled.AuthVersion.Should().Be(3);
    }

    [Fact]
    public async Task IncrementAuthVersionAsync_AndDeleteUserAsync_ShouldHandlePresentAndMissingUsers()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        (await harness.Repository.IncrementAuthVersionAsync(userId)).Should().BeTrue();
        (await harness.Db.Users.SingleAsync(user => user.Id == userId)).AuthVersion.Should().Be(2);
        (await harness.Repository.IncrementAuthVersionAsync(9999)).Should().BeFalse();

        // Deleting a present user returns the blob URLs (here just the avatar) it orphaned;
        // deleting a missing user returns an empty list.
        (await harness.Repository.DeleteUserAsync(userId)).Should().Contain("/avatars/seed.png");
        (await harness.Repository.DeleteUserAsync(userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserAsync_AndGetAuthByEmailAsync_ShouldProjectSanitizedAndAuthViews()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var userId = await harness.SeedUserAsync();

        var user = await harness.Repository.GetUserAsync(userId);
        var auth = await harness.Repository.GetAuthByEmailAsync("seed@example.com");

        user.Should().NotBeNull();
        user!.Password.Should().BeNull();
        user.Usertype.Should().Be("Participant");
        user.Username.Should().Be("seed-user");

        auth.Should().NotBeNull();
        auth!.Password.Should().Be("seed-password");
        auth.Usertype.Should().Be("Participant");
        auth.IsDisabled.Should().BeFalse();
    }

    [Fact]
    public async Task OAuthLookupMethods_ShouldReturnNormalizedRecords()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        await harness.SeedUserAsync(googleId: "google-seed", microsoftId: "ms-seed");

        var byEmail = await harness.Repository.GetOAuthByEmailAsync("seed@example.com");
        var byGoogle = await harness.Repository.GetOAuthByGoogleIdAsync("google-seed");
        var byMicrosoft = await harness.Repository.GetOAuthByMicrosoftIdAsync("ms-seed");

        byEmail.Should().NotBeNull();
        byEmail!.GoogleID.Should().Be("google-seed");
        byEmail.MicrosoftID.Should().Be("ms-seed");
        byEmail.Usertype.Should().Be("Participant");

        byGoogle!.Email.Should().Be("seed@example.com");
        byMicrosoft!.Email.Should().Be("seed@example.com");
    }

    [Fact]
    public async Task GetProfileByUsernameAsync_ShouldFallbackToEmail_WhenUsernameIsBlank()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        await harness.SeedUserAsync(username: "");

        var profile = await harness.Repository.GetProfileByUsernameAsync("");

        profile.Should().NotBeNull();
        profile!.Username.Should().Be("seed@example.com");
        profile.Usertype.Should().Be("Participant");
    }

    [Fact]
    public async Task GetUsersAsync_GetByIdsAsync_AndEmailExistsAsync_ShouldRespectFiltersOrderingAndDetailLevel()
    {
        await using var harness = await AuthUserRepositoryHarness.CreateAsync();
        var firstId = await harness.SeedUserAsync(
            email: "first@example.com",
            role: "Participant",
            username: "first-user",
            disabled: true);
        var secondId = await harness.SeedUserAsync(
            email: "second@example.com",
            role: "Organizer",
            username: "second-user");

        var organizers = await harness.Repository.GetUsersAsync("Organizer", UserReadDetailLevel.Slim);
        var admins = await harness.Repository.GetByIdsAsync([secondId, firstId], UserReadDetailLevel.Admin);

        organizers.Should().ContainSingle();
        organizers[0].Email.Should().Be("second@example.com");
        organizers[0].IsDisabled.Should().BeNull();

        admins.Select(user => user.Id).Should().Equal(secondId, firstId);
        admins[1].IsDisabled.Should().BeTrue();
        admins[1].DisabledReason.Should().Be("disabled");
        admins[1].CreatedAt.Should().NotBeNull();

        (await harness.Repository.GetByIdsAsync([], UserReadDetailLevel.Slim)).Should().BeEmpty();
        (await harness.Repository.EmailExistsAsync("first@example.com")).Should().BeTrue();
        (await harness.Repository.EmailExistsAsync("missing@example.com")).Should().BeFalse();
    }

    private sealed class AuthUserRepositoryHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public AuthUserRepository Repository { get; }

        private AuthUserRepositoryHarness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;
            Repository = new AuthUserRepository(db);
        }

        public static async Task<AuthUserRepositoryHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            return new AuthUserRepositoryHarness(connection, db);
        }

        public async Task<int> SeedUserAsync(
            string email = "seed@example.com",
            string role = "participant",
            string username = "seed-user",
            string? googleId = null,
            string? microsoftId = null,
            bool disabled = false)
        {
            var user = new User
            {
                Email = email,
                Password = "seed-password",
                Usertype = role,
                Name = "Seed User",
                Username = username,
                Avatar = "/avatars/seed.png",
                Address = "1 Seed Street",
                Phone = "555-0000",
                GoogleID = googleId,
                MicrosoftID = microsoftId,
                IsDisabled = disabled,
                DisabledAtUtc = disabled ? DateTime.UtcNow : null,
                DisabledReason = disabled ? "disabled" : null,
                AuthVersion = 1
            };

            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
