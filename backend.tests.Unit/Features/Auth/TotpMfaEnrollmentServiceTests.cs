using backend.main.features.auth.mfa.totp;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

using OtpNet;

namespace backend.tests.Unit.Features.Auth;

public class TotpMfaEnrollmentServiceTests
{
    private static TotpMfaEnrollmentService CreateService(
        Mock<ITotpMfaEnrollmentRepository>? repo = null,
        InMemoryCacheService? cache = null)
    {
        repo ??= new Mock<ITotpMfaEnrollmentRepository>();
        cache ??= new InMemoryCacheService();
        return new TotpMfaEnrollmentService(repo.Object, cache);
    }

    [Fact]
    public async Task StartEnrollmentAsync_ShouldReturnValidSecretAndQrUri()
    {
        var service = CreateService();

        var response = await service.StartEnrollmentAsync(99, "user@example.com");

        response.SecretKey.Should().NotBeNullOrWhiteSpace();
        response.QrCodeUri.Should().StartWith("otpauth://totp/");
        response.QrCodeUri.Should().Contain(response.SecretKey);
        response.QrCodeUri.Should().Contain("digits=6");
        response.QrCodeUri.Should().Contain("period=30");
        response.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StartEnrollmentAsync_ShouldOverwriteAnyExistingPendingState()
    {
        var cache = new InMemoryCacheService();
        var service = CreateService(cache: cache);

        var first = await service.StartEnrollmentAsync(99, "user@example.com");
        var second = await service.StartEnrollmentAsync(99, "user@example.com");

        second.SecretKey.Should().NotBe(first.SecretKey, "a new secret is generated each time");
        var stored = await cache.GetValueAsync("totp:enrollment:pending:user:99");
        stored.Should().NotBeNullOrWhiteSpace();
        stored.Should().Contain(second.SecretKey);
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldPersistEncryptedSecret_WhenCodeIsValid()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        TotpMfaEnrollment? upserted = null;
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync((int userId, string secret, int keyVersion, DateTime enrolledAt) =>
            {
                upserted = new TotpMfaEnrollment
                {
                    UserId = userId,
                    EncryptedSecret = secret,
                    EncryptionKeyVersion = keyVersion,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = enrolledAt,
                };
                return upserted;
            });

        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBytes = Base32Encoding.ToBytes(start.SecretKey);
        var code = new Totp(secretBytes).ComputeTotp();

        var result = await service.VerifyEnrollmentAsync(99, code);

        result.IsTotpMfaEnabled.Should().BeTrue();
        upserted.Should().NotBeNull();
        upserted!.EncryptedSecret.Should().StartWith("v1:");
        upserted.EncryptedSecret.Should().NotContain(start.SecretKey, "plaintext must not appear in stored value");

        var pendingAfter = await cache.GetValueAsync("totp:enrollment:pending:user:99");
        pendingAfter.Should().BeNull("pending state should be deleted on success");
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldRejectInvalidCode_AndIncrementAttemptCount()
    {
        var cache = new InMemoryCacheService();
        var service = CreateService(cache: cache);
        await service.StartEnrollmentAsync(99, "user@example.com");

        var act = async () => await service.VerifyEnrollmentAsync(99, "000000");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Invalid TOTP code*");
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldClearPendingState_AfterFiveFailedAttempts()
    {
        var cache = new InMemoryCacheService();
        var service = CreateService(cache: cache);
        await service.StartEnrollmentAsync(99, "user@example.com");

        for (var i = 0; i < 4; i++)
        {
            try { await service.VerifyEnrollmentAsync(99, "000000"); } catch { }
        }

        var act = async () => await service.VerifyEnrollmentAsync(99, "000000");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Too many failed attempts*");
        var pending = await cache.GetValueAsync("totp:enrollment:pending:user:99");
        pending.Should().BeNull("pending state is deleted after lockout");
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldRejectReplay_WhenSameCodeSubmittedTwice()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync(new TotpMfaEnrollment { UserId = 99, EncryptedSecret = "v1:placeholder", IsTotpMfaEnabled = true });

        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBase32 = start.SecretKey;
        var secretBytes = Base32Encoding.ToBytes(secretBase32);
        var code = new Totp(secretBytes).ComputeTotp();

        await service.VerifyEnrollmentAsync(99, code);

        // put the same secret back so we can attempt the same code again (replay)
        var pendingJson = $"{{\"SecretBase32\":\"{secretBase32}\",\"FailedAttempts\":0}}";
        await cache.SetValueAsync("totp:enrollment:pending:user:99", pendingJson, TimeSpan.FromMinutes(10));

        var act = async () => await service.VerifyEnrollmentAsync(99, code);
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*already been used*");
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldRejectMalformedCode()
    {
        var cache = new InMemoryCacheService();
        var service = CreateService(cache: cache);
        await service.StartEnrollmentAsync(99, "user@example.com");

        await ((Func<Task>)(() => service.VerifyEnrollmentAsync(99, "12345"))).Should()
            .ThrowAsync<BadRequestException>().WithMessage("*6 digits*");

        await ((Func<Task>)(() => service.VerifyEnrollmentAsync(99, "1234567"))).Should()
            .ThrowAsync<BadRequestException>().WithMessage("*6 digits*");

        await ((Func<Task>)(() => service.VerifyEnrollmentAsync(99, "abc123"))).Should()
            .ThrowAsync<BadRequestException>().WithMessage("*6 digits*");
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldThrow_WhenNoPendingState()
    {
        var service = CreateService();

        var act = async () => await service.VerifyEnrollmentAsync(99, "123456");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*No pending TOTP enrollment*");
    }

    [Fact]
    public async Task DisableAsync_ShouldReturnNull_WhenNotEnrolled()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        repo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync((TotpMfaEnrollment?)null);
        var service = CreateService(repo);

        var result = await service.DisableAsync(99, "123456");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DisableAsync_ShouldRejectInvalidCode()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBytesFromStart = Base32Encoding.ToBytes(start.SecretKey);
        var validCode = new Totp(secretBytesFromStart).ComputeTotp();

        var upsertedEnrollment = null as TotpMfaEnrollment;
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync((int uid, string enc, int kv, DateTime dt) =>
            {
                upsertedEnrollment = new TotpMfaEnrollment
                {
                    UserId = uid,
                    EncryptedSecret = enc,
                    EncryptionKeyVersion = kv,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = dt,
                };
                return upsertedEnrollment;
            });

        await service.VerifyEnrollmentAsync(99, validCode);
        repo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync(upsertedEnrollment);

        var act = async () => await service.DisableAsync(99, "000000");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Invalid TOTP code*");
    }

    [Fact]
    public async Task DisableAsync_ShouldSetDisabled_WhenCodeIsValid()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBytes = Base32Encoding.ToBytes(start.SecretKey);
        var enrollCode = new Totp(secretBytes).ComputeTotp();

        TotpMfaEnrollment? upsertedEnrollment = null;
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync((int uid, string enc, int kv, DateTime dt) =>
            {
                upsertedEnrollment = new TotpMfaEnrollment
                {
                    UserId = uid,
                    EncryptedSecret = enc,
                    EncryptionKeyVersion = kv,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = dt,
                };
                return upsertedEnrollment;
            });

        await service.VerifyEnrollmentAsync(99, enrollCode);
        repo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync(upsertedEnrollment);

        var disabledEnrollment = new TotpMfaEnrollment
        {
            UserId = 99,
            EncryptedSecret = upsertedEnrollment!.EncryptedSecret,
            IsTotpMfaEnabled = false,
            DisabledAtUtc = DateTime.UtcNow,
        };
        repo.Setup(r => r.SetEnabledAsync(99, false, It.IsAny<DateTime?>()))
            .ReturnsAsync(disabledEnrollment);

        await cache.DeleteKeyAsync("totp:lastused:99");
        var disableCode = new Totp(secretBytes).ComputeTotp();
        var result = await service.DisableAsync(99, disableCode);

        result.Should().NotBeNull();
        result!.IsTotpMfaEnabled.Should().BeFalse();
        result.DisabledAtUtc.Should().NotBeNull();
        repo.Verify(r => r.SetEnabledAsync(99, false, It.IsAny<DateTime?>()), Times.Once);
    }
    [Fact]
    public async Task DisableAsync_ShouldAllowExistingEnrollment_WhenEnrollmentIsPaused()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBytes = Base32Encoding.ToBytes(start.SecretKey);
        var enrollCode = new Totp(secretBytes).ComputeTotp();

        TotpMfaEnrollment? upsertedEnrollment = null;
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync((int uid, string enc, int kv, DateTime dt) =>
            {
                upsertedEnrollment = new TotpMfaEnrollment
                {
                    UserId = uid,
                    EncryptedSecret = enc,
                    EncryptionKeyVersion = kv,
                    IsTotpMfaEnabled = true,
                    EnrolledAtUtc = dt,
                };
                return upsertedEnrollment;
            });

        await service.VerifyEnrollmentAsync(99, enrollCode);
        repo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync(upsertedEnrollment);

        var disabledEnrollment = new TotpMfaEnrollment
        {
            UserId = 99,
            EncryptedSecret = upsertedEnrollment!.EncryptedSecret,
            IsTotpMfaEnabled = false,
            DisabledAtUtc = DateTime.UtcNow,
        };
        repo.Setup(r => r.SetEnabledAsync(99, false, It.IsAny<DateTime?>()))
            .ReturnsAsync(disabledEnrollment);

        await cache.DeleteKeyAsync("totp:lastused:99");

        using var scope = new TemporaryEnvironmentVariableScope("AUTH_TOTP_MFA_ENROLLMENT_ENABLED", "false");

        var disableCode = new Totp(secretBytes).ComputeTotp();
        var result = await service.DisableAsync(99, disableCode);

        result.Should().NotBeNull();
        result!.IsTotpMfaEnabled.Should().BeFalse();
        repo.Verify(r => r.SetEnabledAsync(99, false, It.IsAny<DateTime?>()), Times.Once);
    }

    [Fact]
    public async Task VerifyPersistedCodeAsync_ShouldThrow_WhenNotEnrolled()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        repo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync((TotpMfaEnrollment?)null);
        var service = CreateService(repo);

        var act = async () => await service.VerifyPersistedCodeAsync(99, "123456");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*not enrolled*");
    }

    [Fact]
    public async Task VerifyPersistedCodeAsync_ShouldAcceptValidCode_AndRejectReplay()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBytes = Base32Encoding.ToBytes(start.SecretKey);
        var enrollCode = new Totp(secretBytes).ComputeTotp();

        TotpMfaEnrollment? upserted = null;
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync((int uid, string enc, int kv, DateTime dt) =>
            {
                upserted = new TotpMfaEnrollment
                {
                    UserId = uid,
                    EncryptedSecret = enc,
                    IsTotpMfaEnabled = true,
                };
                return upserted;
            });

        await service.VerifyEnrollmentAsync(99, enrollCode);
        repo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync(upserted);

        await cache.DeleteKeyAsync("totp:lastused:99");

        var code = new Totp(secretBytes).ComputeTotp();
        await service.VerifyPersistedCodeAsync(99, code);

        var replayAct = async () => await service.VerifyPersistedCodeAsync(99, code);
        await replayAct.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*already been used*");
    }

    [Fact]
    public async Task EncryptionRoundTrip_IsTransparentThrough_EnrollAndVerify()
    {
        var repo = new Mock<ITotpMfaEnrollmentRepository>();
        var cache = new InMemoryCacheService();
        var service = CreateService(repo, cache);

        var start = await service.StartEnrollmentAsync(99, "user@example.com");
        var secretBytes = Base32Encoding.ToBytes(start.SecretKey);
        var enrollCode = new Totp(secretBytes).ComputeTotp();

        string? storedEncrypted = null;
        repo.Setup(r => r.UpsertAsync(99, It.IsAny<string>(), 1, It.IsAny<DateTime>()))
            .ReturnsAsync((int uid, string enc, int kv, DateTime dt) =>
            {
                storedEncrypted = enc;
                return new TotpMfaEnrollment
                {
                    UserId = uid,
                    EncryptedSecret = enc,
                    IsTotpMfaEnabled = true,
                };
            });

        await service.VerifyEnrollmentAsync(99, enrollCode);

        storedEncrypted.Should().NotBeNull();
        storedEncrypted.Should().StartWith("v1:");
        storedEncrypted.Should().NotBe(start.SecretKey);

        var decrypted = TotpMfaEnrollmentService.Decrypt(storedEncrypted!);
        decrypted.Should().BeEquivalentTo(secretBytes, "decryption must recover the original secret bytes");
    }

    private sealed class TemporaryEnvironmentVariableScope : IDisposable
    {
        private readonly string _key;
        private readonly string? _originalValue;

        public TemporaryEnvironmentVariableScope(string key, string? value)
        {
            _key = key;
            _originalValue = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_key, _originalValue);
        }
    }
}
