using backend.main.features.auth.mfa.totp;
using backend.main.features.profile;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The TOTP key-rotation sweep against PostgreSQL: prefix filtering, the conditional write that
/// protects concurrent changes, and a full rotation and rollback through the real repository.
/// </summary>
public class TotpKeyRotationRepositoryTests
{
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public async Task ListSecretsNotUnderKeyAsync_ShouldMatchThePrefixLiterally_AndPageByUserId()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await SeedAsync(context, new Dictionary<int, string>
        {
            [1] = "v2:ab_d:under-active-key",
            // '_' is a LIKE wildcard; this row must not count as under "v2:ab_d:".
            [2] = "v2:abXd:under-another-key",
            [3] = "v1:legacy",
            [4] = "v2:ab_d:also-under-active-key",
        });
        var repository = new TotpMfaEnrollmentRepository(context);

        var firstPage = await repository.ListSecretsNotUnderKeyAsync("v2:ab_d:", afterUserId: 0, batchSize: 1);
        var secondPage = await repository.ListSecretsNotUnderKeyAsync("v2:ab_d:", afterUserId: 2, batchSize: 10);

        firstPage.Should().Equal(new TotpStoredSecret(2, "v2:abXd:under-another-key"));
        secondPage.Should().Equal(new TotpStoredSecret(3, "v1:legacy"));
        (await repository.CountSecretsNotUnderKeyAsync("v2:ab_d:")).Should().Be(2);
    }

    [Fact]
    public async Task ReplaceSecretAsync_ShouldOnlyReplaceTheExactValueThatWasRead()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        await SeedAsync(context, new Dictionary<int, string> { [1] = "v1:original", [2] = "v1:untouched" });
        var repository = new TotpMfaEnrollmentRepository(context);

        var stale = await repository.ReplaceSecretAsync(1, "v1:read-before-a-re-enrollment", "v2:k:new", 2);
        var current = await repository.ReplaceSecretAsync(1, "v1:original", "v2:k:new", 2);
        var missing = await repository.ReplaceSecretAsync(99, "v1:original", "v2:k:new", 2);

        stale.Should().BeFalse();
        current.Should().BeTrue();
        missing.Should().BeFalse();

        var rows = await context.TotpMfaEnrollments.AsNoTracking().OrderBy(e => e.UserId).ToListAsync();
        rows[0].EncryptedSecret.Should().Be("v2:k:new");
        rows[0].EncryptionKeyVersion.Should().Be(2);
        rows[1].EncryptedSecret.Should().Be("v1:untouched");
        rows[1].EncryptionKeyVersion.Should().Be(1);
    }

    [Fact]
    public async Task Sweep_ShouldRotateAndRollBackEveryStoredSecret_WithoutChangingTheirValues()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        var original = new TotpSecretProtector(KeyA);
        var secrets = Enumerable.Range(1, 5).ToDictionary(id => id, id => Enumerable.Repeat((byte)id, 20).ToArray());
        await SeedAsync(context, secrets.ToDictionary(pair => pair.Key, pair => original.Protect(pair.Value)));
        var repository = new TotpMfaEnrollmentRepository(context);

        var rotated = new TotpSecretProtector(KeyB, [KeyA]);
        var rotation = await new TotpKeyRotationRunner(repository, rotated).RunOnceAsync();
        var afterRotation = await ReadSecretsAsync(context);

        var rolledBack = new TotpSecretProtector(KeyA, [KeyB]);
        var rollback = await new TotpKeyRotationRunner(repository, rolledBack).RunOnceAsync();
        var afterRollback = await ReadSecretsAsync(context);

        rotation.Should().Be(new TotpKeyRotationResult(5, 0, 0, 0));
        afterRotation.Values.Should().OnlyContain(value => rotated.IsProtectedWithActiveKey(value));
        rollback.Should().Be(new TotpKeyRotationResult(5, 0, 0, 0));
        foreach (var (userId, secret) in secrets)
            original.Unprotect(afterRollback[userId]).Should().Equal(secret);
    }

    private static async Task SeedAsync(
        backend.main.infrastructure.database.core.AppDatabaseContext context,
        IReadOnlyDictionary<int, string> encryptedSecrets)
    {
        foreach (var (userId, encryptedSecret) in encryptedSecrets)
        {
            context.Users.Add(new User { Id = userId, Email = $"totp-rotation-{userId}@test.local", Usertype = "Participant" });
            context.TotpMfaEnrollments.Add(new TotpMfaEnrollment
            {
                UserId = userId,
                EncryptedSecret = encryptedSecret,
                EncryptionKeyVersion = 1,
                IsTotpMfaEnabled = true,
                EnrolledAtUtc = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task<Dictionary<int, string>> ReadSecretsAsync(
        backend.main.infrastructure.database.core.AppDatabaseContext context) =>
        await context.TotpMfaEnrollments.AsNoTracking().ToDictionaryAsync(e => e.UserId, e => e.EncryptedSecret);
}
