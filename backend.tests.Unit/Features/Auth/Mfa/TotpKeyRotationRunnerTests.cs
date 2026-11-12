using backend.main.features.auth.mfa.totp;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Features.Auth.Mfa;

/// <summary>
/// The TOTP key-rotation sweep: what it re-encrypts, what it leaves alone, and that a rollback
/// sweeps every secret back under the original key without changing any of them.
/// </summary>
public class TotpKeyRotationRunnerTests
{
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyC = Convert.ToBase64String(Enumerable.Range(201, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public async Task RunOnceAsync_ShouldDoNothing_OutsideARotation()
    {
        var repository = new Mock<ITotpMfaEnrollmentRepository>(MockBehavior.Strict);

        var result = await new TotpKeyRotationRunner(repository.Object, new TotpSecretProtector(KeyA)).RunOnceAsync();

        result.Should().Be(new TotpKeyRotationResult(0, 0, 0, 0));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldMoveEverySecretUnderTheActiveKey_AndKeepItsValue()
    {
        var store = new FakeSecretStore();
        var secrets = SeedSecrets(store, new TotpSecretProtector(KeyA), count: 450);
        var rotated = new TotpSecretProtector(KeyB, [KeyA]);

        var result = await new TotpKeyRotationRunner(store, rotated).RunOnceAsync();

        result.Should().Be(new TotpKeyRotationResult(450, 0, 0, 0));
        store.Rows.Values.Should().OnlyContain(value => rotated.IsProtectedWithActiveKey(value));
        foreach (var (userId, secret) in secrets)
            rotated.Unprotect(store.Rows[userId]).Should().Equal(secret);
    }

    [Fact]
    public async Task DistributingANewKey_ShouldLeaveEverySecretUnderTheCurrentKey()
    {
        var store = new FakeSecretStore();
        var secrets = SeedSecrets(store, new TotpSecretProtector(KeyA), count: 3);
        var before = new Dictionary<int, string>(store.Rows);
        var distributing = new TotpSecretProtector(KeyA, [KeyB]);

        var result = await new TotpKeyRotationRunner(store, distributing).RunOnceAsync();

        // Replicas that have not received KeyB yet can still read every row.
        result.Should().Be(new TotpKeyRotationResult(0, 0, 0, 0));
        store.Rows.Should().Equal(before);
        foreach (var (userId, secret) in secrets)
            new TotpSecretProtector(KeyA).Unprotect(store.Rows[userId]).Should().Equal(secret);
    }

    [Fact]
    public async Task Rollback_ShouldSweepSecretsBackUnderTheOriginalKey()
    {
        var store = new FakeSecretStore();
        var secrets = SeedSecrets(store, new TotpSecretProtector(KeyA), count: 3);
        await new TotpKeyRotationRunner(store, new TotpSecretProtector(KeyB, [KeyA])).RunOnceAsync();
        var rolledBack = new TotpSecretProtector(KeyA, [KeyB]);

        var result = await new TotpKeyRotationRunner(store, rolledBack).RunOnceAsync();

        result.ReEncrypted.Should().Be(3);
        result.Remaining.Should().Be(0);
        foreach (var (userId, secret) in secrets)
        {
            rolledBack.IsProtectedWithActiveKey(store.Rows[userId]).Should().BeTrue();
            new TotpSecretProtector(KeyA).Unprotect(store.Rows[userId]).Should().Equal(secret);
        }
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLeaveARowAlone_WhenItChangedDuringThePass()
    {
        var store = new FakeSecretStore();
        SeedSecrets(store, new TotpSecretProtector(KeyA), count: 2);
        var reEnrolled = new TotpSecretProtector(KeyB).Protect([9, 9, 9]);
        store.BeforeReplace = userId =>
        {
            if (userId == 1)
                store.Rows[1] = reEnrolled;
        };

        var result = await new TotpKeyRotationRunner(store, new TotpSecretProtector(KeyB, [KeyA])).RunOnceAsync();

        result.ReEncrypted.Should().Be(1);
        result.ChangedConcurrently.Should().Be(1);
        store.Rows[1].Should().Be(reEnrolled);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldCountButKeepSecretsNoConfiguredKeyCanOpen()
    {
        var store = new FakeSecretStore();
        var orphan = new TotpSecretProtector(KeyC).Protect([1, 2, 3]);
        store.Rows[7] = orphan;

        var result = await new TotpKeyRotationRunner(store, new TotpSecretProtector(KeyB, [KeyA])).RunOnceAsync();

        result.Should().Be(new TotpKeyRotationResult(0, 0, 1, 1));
        store.Rows[7].Should().Be(orphan);
    }

    [Fact]
    public async Task Service_ShouldExitAtOnce_OutsideARotation()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var service = new TotpKeyRotationService(provider, new TotpSecretProtector(KeyA));

        await service.StartAsync(CancellationToken.None);

        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        service.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Service_ShouldSweepAtStartup_WhileARotationIsUnderWay()
    {
        var store = new FakeSecretStore();
        SeedSecrets(store, new TotpSecretProtector(KeyA), count: 3);
        var rotated = new TotpSecretProtector(KeyB, [KeyA]);
        var swept = new TaskCompletionSource();
        store.AfterCount = () => swept.TrySetResult();
        using var provider = new ServiceCollection()
            .AddSingleton(rotated)
            .AddSingleton<ITotpMfaEnrollmentRepository>(store)
            .AddScoped<TotpKeyRotationRunner>()
            .BuildServiceProvider();
        using var service = new TotpKeyRotationService(provider, rotated);

        await service.StartAsync(CancellationToken.None);
        await swept.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        store.Rows.Values.Should().OnlyContain(value => rotated.IsProtectedWithActiveKey(value));
    }

    private static Dictionary<int, byte[]> SeedSecrets(FakeSecretStore store, TotpSecretProtector protector, int count)
    {
        var secrets = new Dictionary<int, byte[]>();
        for (var userId = 1; userId <= count; userId++)
        {
            var secret = BitConverter.GetBytes(userId * 7919).Concat(new byte[16]).ToArray();
            secrets[userId] = secret;
            store.Rows[userId] = protector.Protect(secret);
        }

        return secrets;
    }

    /// <summary>The repository's sweep contract over a dictionary, including the conditional write.</summary>
    private sealed class FakeSecretStore : ITotpMfaEnrollmentRepository
    {
        public SortedDictionary<int, string> Rows { get; } = new();

        public Action<int>? BeforeReplace { get; set; }

        public Task<IReadOnlyList<TotpStoredSecret>> ListSecretsNotUnderKeyAsync(
            string activePrefix, int afterUserId, int batchSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TotpStoredSecret>>(Rows
                .Where(row => row.Key > afterUserId && !row.Value.StartsWith(activePrefix, StringComparison.Ordinal))
                .Take(batchSize)
                .Select(row => new TotpStoredSecret(row.Key, row.Value))
                .ToList());

        public Action? AfterCount { get; set; }

        public Task<int> CountSecretsNotUnderKeyAsync(string activePrefix, CancellationToken cancellationToken = default)
        {
            var count = Rows.Values.Count(value => !value.StartsWith(activePrefix, StringComparison.Ordinal));
            AfterCount?.Invoke();
            return Task.FromResult(count);
        }

        public Task<bool> ReplaceSecretAsync(
            int userId, string expectedEncryptedSecret, string newEncryptedSecret, int keyVersion, CancellationToken cancellationToken = default)
        {
            BeforeReplace?.Invoke(userId);
            if (!Rows.TryGetValue(userId, out var current) || current != expectedEncryptedSecret)
                return Task.FromResult(false);

            Rows[userId] = newEncryptedSecret;
            return Task.FromResult(true);
        }

        public Task<TotpMfaEnrollment?> GetByUserIdAsync(int userId) => throw new NotSupportedException();

        public Task<TotpMfaEnrollment> UpsertAsync(int userId, string encryptedSecret, int keyVersion, DateTime enrolledAtUtc) =>
            throw new NotSupportedException();

        public Task<TotpMfaEnrollment?> SetEnabledAsync(int userId, bool isEnabled, DateTime? disabledAtUtc) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(int userId) => throw new NotSupportedException();
    }
}
