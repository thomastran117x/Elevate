using backend.main.features.profile;
using backend.main.seeders;

using backend.tests.Unit.Support;

using FluentAssertions;

namespace backend.tests.Unit.Seeders;

/// <summary>
/// Pins the username display invariant on the seeder's two write paths.
/// </summary>
/// <remarks>
/// The seeder writes straight through the DbContext rather than going via
/// <c>AuthUserRepository.CreateUserAsync</c>, so it does not inherit that method's display repair
/// and has to establish <c>Normalize(display) == Username</c> itself. It is also the one path that
/// <i>updates</i> a username in place, which is where the two columns can drift apart.
/// </remarks>
public class SeedUsersSeederTests
{
    private const string DefaultPassword = "Password123!";

    // ApplyDefinition verifies the stored hash against the default password, so this has to be a
    // real current-format hash. The minimum Argon2id cost keeps the test fast.
    private static readonly backend.main.features.auth.password.PasswordHasher Hasher =
        TestPasswordHashers.Fast();

    private static readonly string PasswordHash = Hasher.Hash(DefaultPassword);

    private static SeedUserDefinition Definition(string username) =>
        new("seed@example.com", username, "Seed User", "Participant");

    [Fact]
    public void CreateUser_ShouldSetBothFormsFromTheSameValue()
    {
        var user = SeedUsersSeeder.CreateUser(Definition("HarbourOwner"), PasswordHash);

        user.Username.Should().Be("harbourowner");
        user.UsernameDisplay.Should().Be("HarbourOwner");
        UsernamePolicy.IsValidDisplayFor(user.Username!, user.UsernameDisplay).Should().BeTrue();
    }

    /// <summary>
    /// The failure this guards against: updating <c>Username</c> alone leaves the previous name's
    /// display form attached to the new one, and the next seeder run trips
    /// <c>CK_Users_UsernameDisplay_Normalizes</c> and aborts with PostgreSQL 23514.
    /// </summary>
    [Fact]
    public void ApplyDefinition_ShouldMoveBothFormsTogether_WhenTheUsernameChanges()
    {
        var user = SeedUsersSeeder.CreateUser(Definition("OldName"), PasswordHash);

        var changed = SeedUsersSeeder.ApplyDefinition(
            user, Definition("NewName"), DefaultPassword, PasswordHash, Hasher);

        changed.Should().BeTrue();
        user.Username.Should().Be("newname");
        user.UsernameDisplay.Should().Be("NewName");
        UsernamePolicy.IsValidDisplayFor(user.Username!, user.UsernameDisplay).Should().BeTrue();
    }

    /// <summary>
    /// An account renamed in the app carries a display form the catalog never wrote. Reconciling it
    /// back to the seeded name must repair both columns, not just the key.
    /// </summary>
    [Fact]
    public void ApplyDefinition_ShouldRepairADisplayLeftOverFromAnInAppRename()
    {
        var user = SeedUsersSeeder.CreateUser(Definition("SeededName"), PasswordHash);
        user.Username = "renamed-in-app";
        user.UsernameDisplay = "RenamedInApp";

        SeedUsersSeeder.ApplyDefinition(user, Definition("SeededName"), DefaultPassword, PasswordHash, Hasher);

        user.Username.Should().Be("seededname");
        user.UsernameDisplay.Should().Be("SeededName");
        UsernamePolicy.IsValidDisplayFor(user.Username!, user.UsernameDisplay).Should().BeTrue();
    }

    /// <summary>
    /// A row already matching the catalog must report no change, or every run would rewrite rows
    /// and inflate the seeder's updated count.
    /// </summary>
    [Fact]
    public void ApplyDefinition_ShouldReportNoChange_WhenBothFormsAlreadyMatch()
    {
        var user = SeedUsersSeeder.CreateUser(Definition("StableName"), PasswordHash);

        var changed = SeedUsersSeeder.ApplyDefinition(
            user, Definition("StableName"), DefaultPassword, PasswordHash, Hasher);

        changed.Should().BeFalse();
    }

    /// <summary>
    /// Seed accounts created before Argon2id carry BCrypt hashes. They still verify, but the
    /// seeder rewrites them so seeded logins exercise the current format.
    /// </summary>
    [Fact]
    public void ApplyDefinition_ShouldReplaceALegacyBcryptHash()
    {
        var user = SeedUsersSeeder.CreateUser(
            Definition("LegacyHash"),
            BCrypt.Net.BCrypt.HashPassword(DefaultPassword, workFactor: 4));

        var changed = SeedUsersSeeder.ApplyDefinition(
            user, Definition("LegacyHash"), DefaultPassword, PasswordHash, Hasher);

        changed.Should().BeTrue();
        user.Password.Should().Be(PasswordHash);
    }
}
