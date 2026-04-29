using backend.main.dtos;
using backend.main.dtos.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.implementation;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;
using backend.test.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace backend.test;

public class DeviceServiceTests
{
    [Fact]
    public async Task EnsureDeviceKnownAsync_UsesTrustedDeviceSecretInsteadOfBroadClientMatch()
    {
        var repository = new Mock<IDeviceRepository>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{HttpUtility.TrustedDeviceCookieName}=trusted-device-token";

        repository.Setup(client => client.GetDeviceAsync(
                42,
                ComputeDeviceTokenHash("trusted-device-token")
            ))
            .ReturnsAsync(new Device
            {
                Id = 7,
                UserId = 42,
                DeviceTokenHash = ComputeDeviceTokenHash("trusted-device-token"),
                DeviceType = "Desktop",
                ClientName = "Chrome",
                IpAddress = "10.0.0.1",
            });
        repository.Setup(client => client.UpdateLastSeenAsync(It.IsAny<Device>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(repository, publisher, httpContext);

        await service.EnsureDeviceKnownAsync(
            42,
            "user@example.com",
            new ClientRequestInfo
            {
                DeviceType = "Desktop",
                ClientName = "Chrome",
                IpAddress = "10.0.0.2",
                IsBrowserClient = true,
            }
        );

        repository.Verify(client => client.GetDeviceAsync(
            42,
            ComputeDeviceTokenHash("trusted-device-token")
        ));
        publisher.Verify(
            client => client.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()),
            Times.Never
        );
        httpContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName]
            .ToString().Should().Be("trusted-device-token");
    }

    [Fact]
    public async Task VerifyDeviceAsync_PersistsHashedDeviceSecretAndSetsTrustedDeviceCookie()
    {
        var repository = new Mock<IDeviceRepository>();
        var userRepository = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var cacheState = new InMemoryCacheState();
        var httpContext = new DefaultHttpContext();
        var pending = new
        {
            UserId = 42,
            Email = "user@example.com",
            DeviceType = "Desktop",
            ClientName = "Chrome",
            TrustedDeviceId = "DEVICE-SECRET",
            IpAddress = "10.0.0.3",
        };

        cacheState.Values["device:pending:verify-token"] = JsonConvert.SerializeObject(pending);

        repository.Setup(client => client.CreateDeviceAsync(It.IsAny<Device>()))
            .ReturnsAsync((Device device) => device);
        userRepository.Setup(client => client.GetUserAsync(42))
            .ReturnsAsync(new User
            {
                Id = 42,
                Email = "user@example.com",
                Usertype = "attendee",
            });
        tokenService.Setup(client => client.GenerateAccessToken(It.IsAny<User>()))
            .Returns("access-token");
        tokenService.Setup(client => client.GenerateRefreshToken(
                42,
                It.IsAny<ClientRequestInfo>(),
                SessionTransport.BrowserCookie,
                null,
                false
            ))
            .ReturnsAsync(new RefreshTokenIssue(
                "refresh-token",
                "binding-token",
                TimeSpan.FromDays(1),
                SessionTransport.BrowserCookie
            ));

        var service = CreateService(
            repository,
            publisher,
            httpContext,
            cacheState,
            userRepository,
            tokenService
        );

        var result = await service.VerifyDeviceAsync("verify-token", SessionTransport.BrowserCookie);

        result.user.Email.Should().Be("user@example.com");
        repository.Verify(client => client.CreateDeviceAsync(
            It.Is<Device>(device =>
                device.UserId == 42 &&
                device.DeviceTokenHash == ComputeDeviceTokenHash("DEVICE-SECRET") &&
                device.DeviceType == "Desktop" &&
                device.ClientName == "Chrome")
        ));
        httpContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName]
            .ToString().Should().Be("DEVICE-SECRET");
        httpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain($"{HttpUtility.TrustedDeviceCookieName}=DEVICE-SECRET");
    }

    [Fact]
    public async Task EnsureDeviceKnownAsync_WhenTrustedDeviceIsMissing_TriggersVerification()
    {
        var repository = new Mock<IDeviceRepository>();
        var publisher = new Mock<IPublisher>();
        var cacheState = new InMemoryCacheState();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{HttpUtility.TrustedDeviceCookieName}=stale-device-token";

        repository.Setup(client => client.GetDeviceAsync(
                42,
                ComputeDeviceTokenHash("stale-device-token")
            ))
            .ReturnsAsync((Device?)null);
        publisher.Setup(client => client.PublishAsync("eventxperience-email", It.IsAny<EmailMessage>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(repository, publisher, httpContext, cacheState);

        var action = async () => await service.EnsureDeviceKnownAsync(
            42,
            "user@example.com",
            new ClientRequestInfo
            {
                DeviceType = "Desktop",
                ClientName = "Chrome",
                IpAddress = "10.0.0.4",
                IsBrowserClient = true,
            }
        );

        await action.Should().ThrowAsync<DeviceVerificationRequiredException>();
        httpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain($"{HttpUtility.TrustedDeviceCookieName}=;");
    }

    [Fact]
    public async Task EnsureDeviceKnownAsync_StoresPendingVerificationAndPublishesEmail()
    {
        var repository = new Mock<IDeviceRepository>();
        var publisher = new Mock<IPublisher>();
        var cacheState = new InMemoryCacheState();
        var httpContext = new DefaultHttpContext();

        publisher.Setup(client => client.PublishAsync("eventxperience-email", It.IsAny<EmailMessage>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(repository, publisher, httpContext, cacheState);

        var action = async () => await service.EnsureDeviceKnownAsync(
            42,
            "user@example.com",
            new ClientRequestInfo
            {
                DeviceType = "Desktop",
                ClientName = "Chrome",
                IpAddress = "10.0.0.5",
                IsBrowserClient = true,
            }
        );

        await action.Should().ThrowAsync<DeviceVerificationRequiredException>();
        cacheState.Values.Keys.Should().ContainSingle(key => key.StartsWith("device:pending:", StringComparison.Ordinal));
        publisher.Verify(client => client.PublishAsync(
            "eventxperience-email",
            It.Is<EmailMessage>(message =>
                message.Type == EmailMessageType.NewDevice &&
                message.Email == "user@example.com" &&
                !string.IsNullOrWhiteSpace(message.Token))
        ));
    }

    [Fact]
    public async Task VerifyDeviceAsync_ThrowsUnauthorized_WhenPendingVerificationIsMissing()
    {
        var repository = new Mock<IDeviceRepository>();
        var publisher = new Mock<IPublisher>(MockBehavior.Strict);
        var httpContext = new DefaultHttpContext();
        var service = CreateService(repository, publisher, httpContext, new InMemoryCacheState());

        var action = async () => await service.VerifyDeviceAsync(
            "missing-token",
            SessionTransport.BrowserCookie
        );

        await action.Should().ThrowAsync<UnauthorizedException>();
    }

    private static DeviceService CreateService(
        Mock<IDeviceRepository> repository,
        Mock<IPublisher> publisher,
        HttpContext httpContext,
        InMemoryCacheState? cacheState = null,
        Mock<IUserRepository>? userRepository = null,
        Mock<ITokenService>? tokenService = null
    )
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        return new DeviceService(
            repository.Object,
            (userRepository ?? new Mock<IUserRepository>()).Object,
            (tokenService ?? new Mock<ITokenService>()).Object,
            InMemoryCacheMock.Create(cacheState).Object,
            publisher.Object,
            new ClientRequestInfo { IsBrowserClient = true },
            accessor
        );
    }

    private static string ComputeDeviceTokenHash(string deviceToken)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(deviceToken));
        return Convert.ToHexString(bytes);
    }
}
