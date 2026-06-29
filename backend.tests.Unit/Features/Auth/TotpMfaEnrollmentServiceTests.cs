using backend.main.features.auth.mfa.totp;
using backend.main.shared.exceptions.http;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

using OtpNet;

namespace backend.tests.Unit.Features.Auth;

[Collection(EnvironmentVariableTestCollection.Name)]
public class TotpMfaEnrollmentServiceTests
{
    [Fact]
    public async Task StartEnrollmentAsync_ShouldReturnValidSecretAndQrUri()
    {
        using var scope = new EnvironmentVariableScope();
        var service = CreateService();

        var response = await service.StartEnrollmentAsync(99, "user@example.com");

        response.SecretKey.Should().NotBeNullOrWhiteSpace();
        response.QrCodeUri.Should().StartWith("otpauth://totp/");
        response.QrCodeUri.Should().Contain(response.SecretKey);
        response.QrCodeUri.Should().Contain("digits=6");
        response.QrCodeUri.Should().Contain("period=30");
    }

    [Fact]
    public async Task StartEnrollmentAsync_ShouldThrowConflict_WhenTotpIsAlreadyConfigured()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = CreateStatefulRepository(new TotpMfaEnrollment
        {
            UserId = 99,
            EncryptedSecret = "v1:existing",
            EncryptionKeyVersion = 1,
            IsTotpMfaEnabled = true,
            EnrolledAtUtc = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc),
        });
        var service = new TotpMfaEnrollmentService(repository.Object, new InMemoryCacheService());

        var act = () => service.StartEnrollmentAsync(99, "user@example.com");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("TOTP MFA is already configured for this account.");
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldPersistConfiguredSecret_AndClearPendingState()
    {
        using var scope = new EnvironmentVariableScope();
        TotpMfaEnrollment? storedEnrollment = null;
        var repository = CreateStatefulRepository(storedEnrollment, value => storedEnrollment = value);
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(repository.Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var code = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();

        var result = await service.VerifyEnrollmentAsync(99, code);

        result.IsTotpMfaEnabled.Should().BeTrue();
        storedEnrollment.Should().NotBeNull();
        storedEnrollment!.EncryptedSecret.Should().StartWith("v1:");
        storedEnrollment.EncryptedSecret.Should().NotContain(start.SecretKey);
        (await cache.GetValueAsync("totp:enrollment:pending:user:99")).Should().BeNull();
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldRejectMalformedCodes_WithoutClearingPendingState()
    {
        using var scope = new EnvironmentVariableScope();
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(CreateStatefulRepository().Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");

        var act = () => service.VerifyEnrollmentAsync(99, "12A45");

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("TOTP code must be exactly 6 digits.");
        (await cache.GetValueAsync("totp:enrollment:pending:user:99")).Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldClearPendingState_AfterFiveFailedAttempts()
    {
        using var scope = new EnvironmentVariableScope();
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(CreateStatefulRepository().Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var validCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        var invalidCode = InvalidCodeExcept(validCode);

        for (var attempt = 1; attempt < 5; attempt++)
        {
            var act = () => service.VerifyEnrollmentAsync(99, invalidCode);
            await act.Should().ThrowAsync<UnauthorizedException>()
                .WithMessage("Invalid TOTP code. Please check your authenticator app and try again.");
        }

        var finalAttempt = () => service.VerifyEnrollmentAsync(99, invalidCode);
        await finalAttempt.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Too many failed attempts. Please restart the enrollment process.");
        (await cache.GetValueAsync("totp:enrollment:pending:user:99")).Should().BeNull();
    }

    [Fact]
    public async Task EnableAsync_ShouldNotConsumeCode_WhenPersistingEnabledStateFails()
    {
        using var scope = new EnvironmentVariableScope();
        TotpMfaEnrollment? storedEnrollment = null;
        var repository = CreateStatefulRepository(storedEnrollment, value => storedEnrollment = value);
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(repository.Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var enrollCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        await service.VerifyEnrollmentAsync(99, enrollCode);
        await cache.DeleteKeyAsync("totp:lastused:99");

        storedEnrollment!.IsTotpMfaEnabled = false;
        storedEnrollment.DisabledAtUtc = DateTime.UtcNow;

        var shouldFail = true;
        repository.Setup(repo => repo.SetEnabledAsync(99, true, null))
            .ReturnsAsync(() =>
            {
                if (shouldFail)
                {
                    shouldFail = false;
                    return null;
                }

                storedEnrollment = new TotpMfaEnrollment
                {
                    UserId = 99,
                    EncryptedSecret = storedEnrollment!.EncryptedSecret,
                    EncryptionKeyVersion = storedEnrollment.EncryptionKeyVersion,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = storedEnrollment.EnrolledAtUtc,
                    DisabledAtUtc = null,
                };
                return storedEnrollment;
            });

        var code = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();

        var failedEnable = () => service.EnableAsync(99, code);
        await failedEnable.Should().ThrowAsync<ConflictException>()
            .WithMessage("TOTP MFA is not configured for this account.");

        var retriedEnable = await service.EnableAsync(99, code);
        retriedEnable.IsTotpMfaEnabled.Should().BeTrue();
    }
    [Fact]
    public async Task EnableAsync_ShouldThrowConflict_WhenTotpIsNotConfigured()
    {
        using var scope = new EnvironmentVariableScope();
        var service = CreateService();

        var act = () => service.EnableAsync(99, "123456");

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("TOTP MFA is not configured for this account.");
    }

    [Fact]
    public async Task EnableAsync_ShouldRejectCodeFromPendingEnrollment_WhenConfiguredSecretDiffers()
    {
        using var scope = new EnvironmentVariableScope();
        TotpMfaEnrollment? storedEnrollment = null;
        var repository = CreateStatefulRepository(storedEnrollment, value => storedEnrollment = value);
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(repository.Object, cache);

        var initialStart = await service.StartEnrollmentAsync(99, "user@example.com");
        var initialCode = new Totp(Base32Encoding.ToBytes(initialStart.SecretKey)).ComputeTotp();
        await service.VerifyEnrollmentAsync(99, initialCode);

        storedEnrollment!.IsTotpMfaEnabled = false;
        storedEnrollment.DisabledAtUtc = DateTime.UtcNow;
        await cache.DeleteKeyAsync("totp:lastused:99");

        var pendingSecret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        await cache.SetValueAsync(
            "totp:enrollment:pending:user:99",
            $"{{\"SecretBase32\":\"{pendingSecret}\",\"FailedAttempts\":0}}",
            TimeSpan.FromMinutes(10));
        var pendingCode = new Totp(Base32Encoding.ToBytes(pendingSecret)).ComputeTotp();

        var act = () => service.EnableAsync(99, pendingCode);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Invalid TOTP code.");
    }

    [Fact]
    public async Task DisableAsync_ShouldSetConfiguredMethodDisabled_WhenCodeIsValid()
    {
        using var scope = new EnvironmentVariableScope();
        TotpMfaEnrollment? storedEnrollment = null;
        var repository = CreateStatefulRepository(storedEnrollment, value => storedEnrollment = value);
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(repository.Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var enrollCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        await service.VerifyEnrollmentAsync(99, enrollCode);
        await cache.DeleteKeyAsync("totp:lastused:99");

        var disableCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        var result = await service.DisableAsync(99, disableCode);

        result!.IsTotpMfaEnabled.Should().BeFalse();
        result.DisabledAtUtc.Should().NotBeNull();
        storedEnrollment!.IsTotpMfaEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyPersistedCodeAsync_ShouldRejectReplayAsUnauthorized()
    {
        using var scope = new EnvironmentVariableScope();
        TotpMfaEnrollment? storedEnrollment = null;
        var repository = CreateStatefulRepository(storedEnrollment, value => storedEnrollment = value);
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(repository.Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var enrollCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        await service.VerifyEnrollmentAsync(99, enrollCode);
        await cache.DeleteKeyAsync("totp:lastused:99");

        var stepUpCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        await service.VerifyPersistedCodeAsync(99, stepUpCode);

        var replay = () => service.VerifyPersistedCodeAsync(99, stepUpCode);

        await replay.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("TOTP code has already been used. Please wait for a new code.");
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteConfiguredMethod_AndClearReplayState()
    {
        using var scope = new EnvironmentVariableScope();
        TotpMfaEnrollment? storedEnrollment = null;
        var repository = CreateStatefulRepository(storedEnrollment, value => storedEnrollment = value);
        var cache = new InMemoryCacheService();
        var service = new TotpMfaEnrollmentService(repository.Object, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var enrollCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        await service.VerifyEnrollmentAsync(99, enrollCode);
        await cache.DeleteKeyAsync("totp:lastused:99");
        await cache.SetValueAsync("totp:lastused:99", "123", TimeSpan.FromMinutes(1));
        await cache.SetValueAsync("totp:action:attempt:enable:99", "1", TimeSpan.FromMinutes(1));
        await cache.SetValueAsync("totp:action:attempt:disable:99", "1", TimeSpan.FromMinutes(1));
        await cache.SetValueAsync("totp:action:attempt:remove:99", "1", TimeSpan.FromMinutes(1));

        var removeCode = new Totp(Base32Encoding.ToBytes(start.SecretKey)).ComputeTotp();
        await service.RemoveAsync(99, removeCode);

        storedEnrollment.Should().BeNull();
        (await cache.KeyExistsAsync("totp:lastused:99")).Should().BeFalse();
        (await cache.KeyExistsAsync("totp:action:attempt:enable:99")).Should().BeFalse();
        (await cache.KeyExistsAsync("totp:action:attempt:disable:99")).Should().BeFalse();
        (await cache.KeyExistsAsync("totp:action:attempt:remove:99")).Should().BeFalse();
    }

    private static string InvalidCodeExcept(string validCode)
    {
        return validCode == "000000" ? "111111" : "000000";
    }
    private static TotpMfaEnrollmentService CreateService(
        Mock<ITotpMfaEnrollmentRepository>? repository = null,
        InMemoryCacheService? cache = null)
    {
        repository ??= CreateStatefulRepository();
        cache ??= new InMemoryCacheService();
        return new TotpMfaEnrollmentService(repository.Object, cache);
    }

    private static Mock<ITotpMfaEnrollmentRepository> CreateStatefulRepository(
        TotpMfaEnrollment? initial = null,
        Action<TotpMfaEnrollment?>? onChange = null)
    {
        var repository = new Mock<ITotpMfaEnrollmentRepository>();
        TotpMfaEnrollment? stored = initial;
        onChange?.Invoke(stored);

        repository.Setup(repo => repo.GetByUserIdAsync(99))
            .ReturnsAsync(() => stored);
        repository.Setup(repo => repo.UpsertAsync(99, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>()))
            .ReturnsAsync((int userId, string encryptedSecret, int keyVersion, DateTime enrolledAtUtc) =>
            {
                stored = new TotpMfaEnrollment
                {
                    UserId = userId,
                    EncryptedSecret = encryptedSecret,
                    EncryptionKeyVersion = keyVersion,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = enrolledAtUtc,
                    DisabledAtUtc = null,
                };
                onChange?.Invoke(stored);
                return stored;
            });
        repository.Setup(repo => repo.SetEnabledAsync(99, It.IsAny<bool>(), It.IsAny<DateTime?>()))
            .ReturnsAsync((int userId, bool isEnabled, DateTime? disabledAtUtc) =>
            {
                if (stored == null)
                    return null;

                stored = new TotpMfaEnrollment
                {
                    UserId = userId,
                    EncryptedSecret = stored.EncryptedSecret,
                    EncryptionKeyVersion = stored.EncryptionKeyVersion,
                    IsTotpMfaEnabled = isEnabled,
                    EnrolledAtUtc = stored.EnrolledAtUtc,
                    DisabledAtUtc = disabledAtUtc,
                };
                onChange?.Invoke(stored);
                return stored;
            });
        repository.Setup(repo => repo.RemoveAsync(99))
            .ReturnsAsync(() =>
            {
                var existed = stored != null;
                stored = null;
                onChange?.Invoke(stored);
                return existed;
            });

        return repository;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvironmentVariableScope()
        {
            Set("AUTH_TOTP_MFA_ENROLLMENT_ENABLED", "true");
            Set("AUTH_TOTP_ENCRYPTION_KEY", "dW5pdF90ZXN0X3RvdHBfZW5jcnlwdGlvbl9rZXkxMjM=");
        }

        private void Set(string key, string? value)
        {
            _originals[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
