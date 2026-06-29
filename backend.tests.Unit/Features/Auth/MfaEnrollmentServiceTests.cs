using backend.main.features.auth.mfa;
using backend.main.features.auth.notifications;
using backend.main.shared.exceptions.http;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

[Collection(EnvironmentVariableTestCollection.Name)]
public class MfaEnrollmentServiceTests
{
    [Fact]
    public async Task StartEnrollmentAsync_ShouldNormalizePhone_AndSendSmsChallenge()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        var notifications = new Mock<IAuthNotificationService>();
        var cache = new InMemoryCacheService();
        var service = new MfaEnrollmentService(repository.Object, cache, notifications.Object);

        var response = await service.StartEnrollmentAsync(12, "(416) 555-0123");

        response.Channel.Should().Be("sms");
        response.Challenge.Should().NotBeNullOrWhiteSpace();
        response.MaskedDestination.Should().Be("***-***-0123");
        notifications.Verify(notification => notification.SendSmsMfaAsync(
            "+14165550123",
            It.Is<string>(code => code.Length == 6),
            response.Challenge,
            response.ExpiresAtUtc,
            "mfa enrollment"), Times.Once);
    }

    [Fact]
    public async Task StartEnableAsync_ShouldThrowConflict_WhenSmsIsNotConfigured()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        repository.Setup(repo => repo.GetByUserIdAsync(12)).ReturnsAsync((SmsMfaEnrollment?)null);
        var service = new MfaEnrollmentService(repository.Object, new InMemoryCacheService(), Mock.Of<IAuthNotificationService>());

        var act = () => service.StartEnableAsync(12);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("SMS MFA is not configured for this account.");
    }

    [Fact]
    public async Task StartEnableAsync_ShouldReuseStoredPhone_WhenSmsIsDisabled()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        var notifications = new Mock<IAuthNotificationService>();
        repository.Setup(repo => repo.GetByUserIdAsync(12)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = 12,
            PhoneNumber = "+14165550123",
            IsSmsMfaEnabled = false,
            PhoneVerifiedAtUtc = new DateTime(2026, 6, 22, 15, 0, 0, DateTimeKind.Utc),
        });

        var service = new MfaEnrollmentService(repository.Object, new InMemoryCacheService(), notifications.Object);

        var response = await service.StartEnableAsync(12);

        response.MaskedDestination.Should().Be("***-***-0123");
        notifications.Verify(notification => notification.SendSmsMfaAsync(
            "+14165550123",
            It.Is<string>(code => code.Length == 6),
            response.Challenge,
            response.ExpiresAtUtc,
            "mfa re-enable"), Times.Once);
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldEnableSmsMfa_WhenCodeMatchesPendingChallenge()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        var notifications = new Mock<IAuthNotificationService>();
        var cache = new InMemoryCacheService();
        SmsMfaEnrollment? savedEnrollment = null;

        repository.Setup(repo => repo.UpsertVerifiedPhoneAsync(12, "+14165550123", It.IsAny<DateTime>()))
            .ReturnsAsync((int userId, string phoneNumber, DateTime verifiedAtUtc) =>
            {
                savedEnrollment = new SmsMfaEnrollment
                {
                    UserId = userId,
                    PhoneNumber = phoneNumber,
                    IsSmsMfaEnabled = true,
                    PhoneVerifiedAtUtc = verifiedAtUtc,
                    CreatedAt = verifiedAtUtc,
                    UpdatedAt = verifiedAtUtc,
                };
                return savedEnrollment;
            });

        var service = new MfaEnrollmentService(repository.Object, cache, notifications.Object);
        var challenge = await service.StartEnrollmentAsync(12, "+14165550123");
        var sendInvocation = notifications.Invocations.Single();
        var smsCode = sendInvocation.Arguments[1].Should().BeOfType<string>().Subject;

        var enrollment = await service.VerifyEnrollmentAsync(12, smsCode, challenge.Challenge);

        enrollment.IsSmsMfaEnabled.Should().BeTrue();
        enrollment.PhoneNumber.Should().Be("+14165550123");
        savedEnrollment.Should().NotBeNull();
        savedEnrollment!.PhoneVerifiedAtUtc.Should().NotBeNull();
        (await cache.GetValueAsync($"mfa:enrollment:challenge:{challenge.Challenge}")).Should().BeNull();
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldPreserveVerifiedAt_WhenReEnablingExistingPhone()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        var notifications = new Mock<IAuthNotificationService>();
        var cache = new InMemoryCacheService();
        var verifiedAtUtc = new DateTime(2026, 6, 22, 15, 0, 0, DateTimeKind.Utc);

        repository.Setup(repo => repo.GetByUserIdAsync(12)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = 12,
            PhoneNumber = "+14165550123",
            IsSmsMfaEnabled = false,
            PhoneVerifiedAtUtc = verifiedAtUtc,
        });
        repository.Setup(repo => repo.UpsertVerifiedPhoneAsync(12, "+14165550123", verifiedAtUtc))
            .ReturnsAsync(new SmsMfaEnrollment
            {
                UserId = 12,
                PhoneNumber = "+14165550123",
                IsSmsMfaEnabled = true,
                PhoneVerifiedAtUtc = verifiedAtUtc,
            });

        var service = new MfaEnrollmentService(repository.Object, cache, notifications.Object);
        var challenge = await service.StartEnableAsync(12);
        var sendInvocation = notifications.Invocations.Single();
        var smsCode = sendInvocation.Arguments[1].Should().BeOfType<string>().Subject;

        var enrollment = await service.VerifyEnrollmentAsync(12, smsCode, challenge.Challenge);

        enrollment.PhoneVerifiedAtUtc.Should().Be(verifiedAtUtc);
        repository.Verify(repo => repo.UpsertVerifiedPhoneAsync(12, "+14165550123", verifiedAtUtc), Times.Once);
    }

    [Fact]
    public async Task VerifyEnrollmentAsync_ShouldClearPendingState_AfterFiveFailedAttempts()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        var notifications = new Mock<IAuthNotificationService>();
        var cache = new InMemoryCacheService();
        var service = new MfaEnrollmentService(repository.Object, cache, notifications.Object);

        var challenge = await service.StartEnrollmentAsync(12, "+14165550123");
        var sendInvocation = notifications.Invocations.Single();
        var validCode = sendInvocation.Arguments[1].Should().BeOfType<string>().Subject;
        var invalidCode = validCode == "000000" ? "111111" : "000000";

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var act = () => service.VerifyEnrollmentAsync(12, invalidCode, challenge.Challenge);
            await act.Should().ThrowAsync<UnauthorizedException>()
                .WithMessage("Invalid or expired MFA enrollment code.");
        }

        (await cache.GetValueAsync($"mfa:enrollment:challenge:{challenge.Challenge}")).Should().BeNull();
        (await cache.GetValueAsync("mfa:enrollment:user:12")).Should().BeNull();
    }
    [Fact]
    public async Task DisableAsync_ShouldThrowConflict_WhenSmsIsAlreadyDisabled()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        repository.Setup(repo => repo.GetByUserIdAsync(12)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = 12,
            PhoneNumber = "+14165550123",
            IsSmsMfaEnabled = false,
            PhoneVerifiedAtUtc = new DateTime(2026, 6, 22, 15, 0, 0, DateTimeKind.Utc),
        });

        var service = new MfaEnrollmentService(repository.Object, new InMemoryCacheService(), Mock.Of<IAuthNotificationService>());

        var act = () => service.DisableAsync(12);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("SMS MFA is already disabled for this account.");
    }

    [Fact]
    public async Task RemoveAsync_ShouldDeleteStoredEnrollment_WhenSmsIsConfigured()
    {
        using var scope = new EnvironmentVariableScope();
        var repository = new Mock<IMfaEnrollmentRepository>();
        repository.Setup(repo => repo.GetByUserIdAsync(12)).ReturnsAsync(new SmsMfaEnrollment
        {
            UserId = 12,
            PhoneNumber = "+14165550123",
            IsSmsMfaEnabled = true,
            PhoneVerifiedAtUtc = new DateTime(2026, 6, 22, 15, 0, 0, DateTimeKind.Utc),
        });
        repository.Setup(repo => repo.RemoveAsync(12)).ReturnsAsync(true);

        var service = new MfaEnrollmentService(repository.Object, new InMemoryCacheService(), Mock.Of<IAuthNotificationService>());

        await service.RemoveAsync(12);

        repository.Verify(repo => repo.RemoveAsync(12), Times.Once);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvironmentVariableScope()
        {
            Set("AUTH_SMS_MFA_ENROLLMENT_ENABLED", "true");
            Set("JWT_SECRET_VERIFICATION", "mfa_enrollment_test_secret_12345678901234567890");
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
