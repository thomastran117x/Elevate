using System.Text;
using System.Text.Json;

using backend.main.features.auth;
using backend.main.features.auth.device;
using backend.main.features.auth.notifications;
using backend.main.features.auth.stepup;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.app;
using backend.main.shared.exceptions.http;
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

        await service.EnsureDeviceKnownAsync(4, "known@example.com", 2, TestRequestInfoFactory.Browser());

        notifications.Verify(s => s.SendDeviceVerificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        cache.Verify(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task EnsureDeviceKnownAsync_ShouldPublishVerificationForUnknownDevices()
    {
        var deviceTrustService = new Mock<IDeviceTrustService>();
        deviceTrustService.Setup(s => s.IsTrustedAsync(7, It.IsAny<ClientRequestInfo>()))
            .ReturnsAsync(false);

        string? storedPending = null;
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(service => service.SetValueAsync(
                It.Is<string>(key => key.StartsWith("device:pending:")),
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>()))
            .Callback<string, string, TimeSpan?>((_, value, _) => storedPending = value)
            .ReturnsAsync(true);

        var notifications = new Mock<IAuthNotificationService>();
        var service = CreateService(
            deviceTrustService: deviceTrustService,
            cacheService: cache,
            authNotificationService: notifications);

        var act = () => service.EnsureDeviceKnownAsync(7, "unknown@example.com", 5, TestRequestInfoFactory.Browser());

        await act.Should().ThrowAsync<DeviceVerificationRequiredException>();
        notifications.Verify(s => s.SendDeviceVerificationAsync(
            "unknown@example.com",
            It.Is<string>(token => !string.IsNullOrWhiteSpace(token)),
            It.IsAny<string?>()), Times.Once);
        JsonDocument.Parse(storedPending!).RootElement.GetProperty("AuthVersion").GetInt32()
            .Should().Be(5);
    }

    [Fact]
    public async Task VerifyDeviceAsync_ShouldPersistDeviceAndReturnAuthSession()
    {
        var pendingState = JsonSerializer.Serialize(new
        {
            UserId = 11,
            AuthVersion = 1,
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
        deviceTrustService.Setup(s => s.TrustAsync(11, user.AuthVersion, "trusted-device-id", "Desktop", "Chrome", "127.0.0.1"))
            .ReturnsAsync(true);

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
        deviceTrustService.Verify(t => t.TrustAsync(11, user.AuthVersion, "trusted-device-id", "Desktop", "Chrome", "127.0.0.1"), Times.Once);
    }

    /// <summary>
    /// A refused trust means the account rotated after the session was issued, so the session's
    /// revocation was missed and has to be repeated here.
    /// </summary>
    [Fact]
    public async Task VerifyDeviceAsync_ShouldRevokeAndRefuse_WhenTrustIsRefused()
    {
        var harness = VerifyHarness.Create(PendingState());
        harness.DeviceTrust
            .Setup(s => s.TrustAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var act = () => harness.Service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("*no longer valid*");
        harness.Tokens.Verify(t => t.RevokeAllRefreshSessionsAsync(11), Times.Once);
    }

    [Fact]
    public async Task VerifyDeviceAsync_ShouldNotTrustTheDevice_ForADisabledAccount()
    {
        var harness = VerifyHarness.Create(PendingState());
        harness.User.IsDisabled = true;

        var act = () => harness.Service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<ForbiddenException>();
        harness.DeviceTrust.Verify(
            s => s.TrustAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        harness.Sessions.Verify(
            s => s.IssueAsync(It.IsAny<backend.main.features.profile.User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);
    }

    /// <summary>
    /// A link that outlives a password reset or email change must not still sign its holder in,
    /// nor trust their device.
    /// </summary>
    [Fact]
    public async Task VerifyDeviceAsync_ShouldRefuse_WhenTheAccountRotatedSinceTheLinkWasSent()
    {
        var harness = VerifyHarness.Create(PendingState());
        harness.User.AuthVersion = 2;

        var act = () => harness.Service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("*no longer valid*");
        harness.Sessions.Verify(
            s => s.IssueAsync(It.IsAny<backend.main.features.profile.User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);
        harness.DeviceTrust.Verify(
            s => s.TrustAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Entries cached before the version was recorded cannot show they predate no rotation.
    /// </summary>
    [Fact]
    public async Task VerifyDeviceAsync_ShouldRefuse_ALinkStoredWithoutAnAuthVersion()
    {
        var harness = VerifyHarness.Create(PendingState(authVersion: null));

        var act = () => harness.Service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("*no longer valid*");
        harness.Sessions.Verify(
            s => s.IssueAsync(It.IsAny<backend.main.features.profile.User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()),
            Times.Never);
    }

    private static string PendingState(int? authVersion = 1) => JsonSerializer.Serialize(new
    {
        UserId = 11,
        AuthVersion = authVersion,
        Email = "member@example.com",
        DeviceType = "Desktop",
        ClientName = "Chrome",
        TrustedDeviceId = "trusted-device-id",
        IpAddress = "127.0.0.1"
    });

    /// <summary>
    /// A pending device verification for account 11 under "verify-token", with every collaborator
    /// set up to succeed. Tests override the piece they are about.
    /// </summary>
    private sealed class VerifyHarness
    {
        public required DeviceService Service { get; init; }
        public required backend.main.features.profile.User User { get; init; }
        public required Mock<IDeviceTrustService> DeviceTrust { get; init; }
        public required Mock<IAuthSessionService> Sessions { get; init; }
        public required Mock<ITokenService> Tokens { get; init; }

        public static VerifyHarness Create(string pendingState)
        {
            var cache = new Mock<backend.main.features.cache.ICacheService>();
            cache.Setup(s => s.GetValueAsync("device:pending:verify-token"))
                .ReturnsAsync(pendingState);
            cache.Setup(s => s.DeleteKeyAsync("device:pending:verify-token"))
                .ReturnsAsync(true);

            var user = new TestUserBuilder().WithId(11).WithEmail("member@example.com").Build();
            var userRepository = new Mock<IAuthUserRepository>();
            userRepository.Setup(repo => repo.GetUserAsync(11)).ReturnsAsync(user);

            var loginStepUpChallengeService = new Mock<ILoginStepUpChallengeService>();
            loginStepUpChallengeService.Setup(s => s.TryVerifyEmailAsync("verify-token"))
                .ReturnsAsync((AuthenticatedSessionResult?)null);

            var deviceTrust = new Mock<IDeviceTrustService>();
            deviceTrust
                .Setup(s => s.TrustAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var sessions = new Mock<IAuthSessionService>();
            sessions
                .Setup(s => s.IssueAsync(It.IsAny<backend.main.features.profile.User>(), It.IsAny<SessionTransport>(), It.IsAny<string?>(), It.IsAny<bool?>()))
                .ReturnsAsync(new UserToken(
                    new Token("access-token", DateTime.UtcNow.AddMinutes(15), "refresh-token", "binding-token", TimeSpan.FromDays(1), SessionTransport.BrowserCookie),
                    user));

            var tokens = new Mock<ITokenService>();

            return new VerifyHarness
            {
                Service = CreateService(
                    userRepository: userRepository,
                    cacheService: cache,
                    loginStepUpChallengeService: loginStepUpChallengeService,
                    deviceTrustService: deviceTrust,
                    authSessionService: sessions,
                    tokenService: tokens),
                User = user,
                DeviceTrust = deviceTrust,
                Sessions = sessions,
                Tokens = tokens
            };
        }
    }

    private static DeviceService CreateService(
        Mock<IDeviceRepository>? deviceRepository = null,
        Mock<IAuthUserRepository>? userRepository = null,
        Mock<backend.main.features.cache.ICacheService>? cacheService = null,
        Mock<IAuthNotificationService>? authNotificationService = null,
        Mock<IDeviceTrustService>? deviceTrustService = null,
        Mock<IAuthSessionService>? authSessionService = null,
        Mock<ILoginStepUpChallengeService>? loginStepUpChallengeService = null,
        Mock<ITokenService>? tokenService = null)
    {
        deviceRepository ??= new Mock<IDeviceRepository>();
        userRepository ??= new Mock<IAuthUserRepository>();
        cacheService ??= new Mock<backend.main.features.cache.ICacheService>();
        authNotificationService ??= new Mock<IAuthNotificationService>();
        deviceTrustService ??= new Mock<IDeviceTrustService>();
        authSessionService ??= new Mock<IAuthSessionService>();
        loginStepUpChallengeService ??= new Mock<ILoginStepUpChallengeService>();
        tokenService ??= new Mock<ITokenService>();

        return new DeviceService(
            deviceRepository.Object,
            userRepository.Object,
            cacheService.Object,
            authNotificationService.Object,
            TestRequestInfoFactory.Browser(),
            deviceTrustService.Object,
            authSessionService.Object,
            loginStepUpChallengeService.Object,
            tokenService.Object);
    }

    private static string ComputeHash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
