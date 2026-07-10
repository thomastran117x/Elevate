using System.Text;
using System.Text.Json;

using backend.main.features.auth;
using backend.main.features.auth.device;
using backend.main.features.auth.notifications;
using backend.main.features.auth.stepup;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.app;
using backend.main.shared.requests;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class DeviceServiceTests
{
    [Fact]
    public async Task EnsureDeviceKnownAsync_ShouldReuseKnownTrustedDevice()
    {
        var deviceTrustService = new Mock<IDeviceTrustService>();
        deviceTrustService.Setup(s => s.IsTrustedAsync(4, It.IsAny<ClientRequestInfo>()))
            .ReturnsAsync(true);

        var notifications = new Mock<IAuthNotificationService>();
        var cache = new Mock<backend.main.features.cache.ICacheService>();

        var service = CreateService(
            deviceTrustService: deviceTrustService,
            cacheService: cache,
            authNotificationService: notifications);

        await service.EnsureDeviceKnownAsync(4, "known@example.com", TestRequestInfoFactory.Browser());

        notifications.Verify(s => s.SendDeviceVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        cache.Verify(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task EnsureDeviceKnownAsync_ShouldPublishVerificationForUnknownDevices()
    {
        var deviceTrustService = new Mock<IDeviceTrustService>();
        deviceTrustService.Setup(s => s.IsTrustedAsync(7, It.IsAny<ClientRequestInfo>()))
            .ReturnsAsync(false);

        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(service => service.SetValueAsync(
                It.Is<string>(key => key.StartsWith("device:pending:")),
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var notifications = new Mock<IAuthNotificationService>();
        var service = CreateService(
            deviceTrustService: deviceTrustService,
            cacheService: cache,
            authNotificationService: notifications);

        var act = () => service.EnsureDeviceKnownAsync(7, "unknown@example.com", TestRequestInfoFactory.Browser());

        await act.Should().ThrowAsync<DeviceVerificationRequiredException>();
        notifications.Verify(s => s.SendDeviceVerificationAsync(
            "unknown@example.com",
            It.Is<string>(token => !string.IsNullOrWhiteSpace(token)),
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task VerifyDeviceAsync_ShouldPersistDeviceAndReturnAuthSession()
    {
        var pendingState = JsonSerializer.Serialize(new
        {
            UserId = 11,
            Email = "member@example.com",
            DeviceType = "Desktop",
            ClientName = "Chrome",
            TrustedDeviceId = "trusted-device-id",
            IpAddress = "127.0.0.1"
        });

        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(s => s.GetValueAsync("device:pending:verify-token"))
            .ReturnsAsync(pendingState);
        cache.Setup(s => s.DeleteKeyAsync("device:pending:verify-token"))
            .ReturnsAsync(true);

        var userRepository = new Mock<IAuthUserRepository>();
        var user = new TestUserBuilder().WithId(11).WithEmail("member@example.com").Build();
        userRepository.Setup(repo => repo.GetUserAsync(11))
            .ReturnsAsync(user);

        var loginStepUpChallengeService = new Mock<ILoginStepUpChallengeService>();
        loginStepUpChallengeService.Setup(s => s.TryVerifyEmailAsync("verify-token"))
            .ReturnsAsync((AuthenticatedSessionResult?)null);

        var deviceTrustService = new Mock<IDeviceTrustService>();
        deviceTrustService.Setup(s => s.TrustAsync(11, "trusted-device-id", "Desktop", "Chrome", "127.0.0.1"))
            .Returns(Task.CompletedTask);

        var expectedToken = new Token(
            "access-token",
            DateTime.UtcNow.AddMinutes(15),
            "refresh-token",
            "binding-token",
            TimeSpan.FromDays(1),
            SessionTransport.BrowserCookie);
        var authSessionService = new Mock<IAuthSessionService>();
        authSessionService.Setup(s => s.IssueAsync(
                It.Is<backend.main.features.profile.User>(u => u.Id == 11),
                SessionTransport.BrowserCookie,
                It.IsAny<string?>(),
                It.IsAny<bool?>()))
            .ReturnsAsync(new UserToken(expectedToken, user));

        var service = CreateService(
            userRepository: userRepository,
            cacheService: cache,
            loginStepUpChallengeService: loginStepUpChallengeService,
            deviceTrustService: deviceTrustService,
            authSessionService: authSessionService);

        var result = await service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        result.UserToken.user.Email.Should().Be("member@example.com");
        result.UserToken.token.RefreshToken.Should().Be("refresh-token");
        deviceTrustService.Verify(t => t.TrustAsync(11, "trusted-device-id", "Desktop", "Chrome", "127.0.0.1"), Times.Once);
    }

    private static DeviceService CreateService(
        Mock<IDeviceRepository>? deviceRepository = null,
        Mock<IAuthUserRepository>? userRepository = null,
        Mock<backend.main.features.cache.ICacheService>? cacheService = null,
        Mock<IAuthNotificationService>? authNotificationService = null,
        Mock<IDeviceTrustService>? deviceTrustService = null,
        Mock<IAuthSessionService>? authSessionService = null,
        Mock<ILoginStepUpChallengeService>? loginStepUpChallengeService = null)
    {
        deviceRepository ??= new Mock<IDeviceRepository>();
        userRepository ??= new Mock<IAuthUserRepository>();
        cacheService ??= new Mock<backend.main.features.cache.ICacheService>();
        authNotificationService ??= new Mock<IAuthNotificationService>();
        deviceTrustService ??= new Mock<IDeviceTrustService>();
        authSessionService ??= new Mock<IAuthSessionService>();
        loginStepUpChallengeService ??= new Mock<ILoginStepUpChallengeService>();

        return new DeviceService(
            deviceRepository.Object,
            userRepository.Object,
            cacheService.Object,
            authNotificationService.Object,
            TestRequestInfoFactory.Browser(),
            deviceTrustService.Object,
            authSessionService.Object,
            loginStepUpChallengeService.Object);
    }

    private static string ComputeHash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
