using System.Text;
using System.Text.Json;

using backend.main.features.auth;
using backend.main.features.auth.device;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.app;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.utilities;

using backend.tests.Unit.Support;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class DeviceServiceTests
{
    [Fact]
    public async Task EnsureDeviceKnownAsync_ShouldReuseKnownTrustedDevice()
    {
        const string trustedDeviceToken = "trusted-device-token";
        var existingDevice = new Device
        {
            UserId = 4,
            DeviceTokenHash = ComputeHash(trustedDeviceToken),
            DeviceType = "Desktop",
            ClientName = "Chrome",
            IpAddress = "127.0.0.1"
        };

        var repository = new Mock<IDeviceRepository>();
        repository.Setup(repo => repo.GetDeviceAsync(4, existingDevice.DeviceTokenHash))
            .ReturnsAsync(existingDevice);

        var publisher = new Mock<IPublisher>();
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HttpUtility.TrustedDeviceHeaderName] = trustedDeviceToken;

        var service = CreateService(
            deviceRepository: repository,
            cacheService: cache,
            publisher: publisher,
            httpContext: httpContext);

        await service.EnsureDeviceKnownAsync(4, "known@example.com", TestRequestInfoFactory.Browser());

        repository.Verify(repo => repo.UpdateLastSeenAsync(
            It.Is<Device>(device => device.IpAddress == "127.0.0.1"
                && device.DeviceType == "Desktop"
                && device.ClientName == "Chrome")), Times.Once);
        publisher.Verify(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()), Times.Never);
        cache.Verify(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task EnsureDeviceKnownAsync_ShouldPublishVerificationForUnknownDevices()
    {
        var cache = new Mock<backend.main.features.cache.ICacheService>();
        cache.Setup(service => service.SetValueAsync(
                It.Is<string>(key => key.StartsWith("device:pending:")),
                It.IsAny<string>(),
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);

        var publisher = new Mock<IPublisher>();
        var service = CreateService(cacheService: cache, publisher: publisher);

        var act = () => service.EnsureDeviceKnownAsync(7, "unknown@example.com", TestRequestInfoFactory.Browser());

        await act.Should().ThrowAsync<DeviceVerificationRequiredException>();
        publisher.Verify(p => p.PublishAsync(
            "eventxperience-email",
            It.Is<EmailMessage>(message => message.Type == EmailMessageType.NewDevice
                && message.Email == "unknown@example.com"
                && !string.IsNullOrWhiteSpace(message.Token))),
            Times.Once);
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
        cache.Setup(service => service.GetValueAsync("device:pending:verify-token"))
            .ReturnsAsync(pendingState);
        cache.Setup(service => service.DeleteKeyAsync("device:pending:verify-token"))
            .ReturnsAsync(true);

        var repository = new Mock<IDeviceRepository>();
        var userRepository = new Mock<IAuthUserRepository>();
        userRepository.Setup(repo => repo.GetUserAsync(11))
            .ReturnsAsync(new TestUserBuilder().WithId(11).WithEmail("member@example.com").Build());

        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.GenerateAccessToken(It.IsAny<backend.main.features.profile.User>()))
            .Returns(new AccessTokenIssue("access-token", DateTime.UtcNow.AddMinutes(15)));
        tokenService.Setup(service => service.GenerateRefreshToken(
                11,
                It.IsAny<backend.main.shared.requests.ClientRequestInfo>(),
                SessionTransport.BrowserCookie,
                null,
                false))
            .ReturnsAsync(new RefreshTokenIssue(
                "refresh-token",
                "binding-token",
                TimeSpan.FromDays(1),
                SessionTransport.BrowserCookie));

        var httpContext = new DefaultHttpContext();
        var service = CreateService(
            deviceRepository: repository,
            userRepository: userRepository,
            tokenService: tokenService,
            cacheService: cache,
            httpContext: httpContext);

        var result = await service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        result.user.Email.Should().Be("member@example.com");
        result.token.RefreshToken.Should().Be("refresh-token");
        repository.Verify(repo => repo.CreateDeviceAsync(
            It.Is<Device>(device => device.UserId == 11
                && device.DeviceType == "Desktop"
                && device.ClientName == "Chrome"
                && device.IpAddress == "127.0.0.1")), Times.Once);
        httpContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName].ToString()
            .Should().Be("trusted-device-id");
    }

    private static DeviceService CreateService(
        Mock<IDeviceRepository>? deviceRepository = null,
        Mock<IAuthUserRepository>? userRepository = null,
        Mock<ITokenService>? tokenService = null,
        Mock<backend.main.features.cache.ICacheService>? cacheService = null,
        Mock<IPublisher>? publisher = null,
        HttpContext? httpContext = null)
    {
        deviceRepository ??= new Mock<IDeviceRepository>();
        userRepository ??= new Mock<IAuthUserRepository>();
        tokenService ??= new Mock<ITokenService>();
        cacheService ??= new Mock<backend.main.features.cache.ICacheService>();
        publisher ??= new Mock<IPublisher>();

        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext ?? new DefaultHttpContext()
        };

        return new DeviceService(
            deviceRepository.Object,
            userRepository.Object,
            tokenService.Object,
            cacheService.Object,
            publisher.Object,
            TestRequestInfoFactory.Browser(),
            accessor);
    }

    private static string ComputeHash(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
