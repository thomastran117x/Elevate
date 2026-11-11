using backend.main.features.auth.device;
using backend.main.features.auth.events;
using backend.main.shared.requests;
using backend.main.shared.utilities;
using backend.main.utilities;

using backend.tests.Unit.Features.Auth.Events;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class DeviceTrustServiceTests
{
    [Fact]
    public async Task IsTrustedAsync_ShouldReturnFalse_WhenNoHttpContext()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var service = new DeviceTrustService(
            new Mock<IDeviceRepository>().Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        var result = await service.IsTrustedAsync(1, TestRequestInfoFactory.Browser());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsTrustedAsync_ShouldReturnFalse_WhenNoTrustedDeviceTokenHeader()
    {
        var httpContext = new DefaultHttpContext();

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var service = new DeviceTrustService(
            new Mock<IDeviceRepository>().Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        var result = await service.IsTrustedAsync(1, TestRequestInfoFactory.Browser());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TrustAsync_ShouldTrustTheDevice_AtTheExpectedAuthVersion()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository
            .Setup(r => r.TrustIfAuthVersionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Device>()))
            .ReturnsAsync(true);

        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        var trusted = await service.TrustAsync(3, 4, "device-id-abc", "Desktop", "Chrome", "127.0.0.1");

        trusted.Should().BeTrue();
        deviceRepository.Verify(r => r.TrustIfAuthVersionAsync(3, 4, It.Is<Device>(d =>
            d.UserId == 3
            && d.DeviceTokenHash == CryptoHelper.HashToken("device-id-abc")
            && d.DeviceType == "Desktop"
            && d.ClientName == "Chrome"
            && d.IpAddress == "127.0.0.1")), Times.Once);
    }

    [Fact]
    public async Task TrustAsync_ShouldHandTheDeviceTokenBack_WhenTrusted()
    {
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository
            .Setup(r => r.TrustIfAuthVersionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Device>()))
            .ReturnsAsync(true);

        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        await service.TrustAsync(3, 4, "device-id-abc", "Desktop", "Chrome", "127.0.0.1");

        httpContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName].ToString()
            .Should().Be("device-id-abc");
        httpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain(HttpUtility.TrustedDeviceCookieName);
    }

    /// <summary>
    /// A refused trust must leave the client with nothing to present on its next sign-in.
    /// </summary>
    [Fact]
    public async Task TrustAsync_ShouldNotHandTheDeviceTokenBack_WhenTheAuthVersionMovedOn()
    {
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);

        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository
            .Setup(r => r.TrustIfAuthVersionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Device>()))
            .ReturnsAsync(false);

        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        var trusted = await service.TrustAsync(3, 4, "device-id-abc", "Desktop", "Chrome", "127.0.0.1");

        trusted.Should().BeFalse();
        httpContext.Response.Headers.ContainsKey(HttpUtility.TrustedDeviceHeaderName).Should().BeFalse();
        httpContext.Response.Headers.ContainsKey("Set-Cookie").Should().BeFalse();
    }
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TrustAsync_ShouldTrustForNinetyDaysFromTheVerification()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);
        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository
            .Setup(r => r.TrustIfAuthVersionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Device>()))
            .ReturnsAsync(true);
        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser(),
            new FixedClock(Now));

        await service.TrustAsync(3, 4, "device-id-abc", "Desktop", "Chrome", "127.0.0.1");

        deviceRepository.Verify(r => r.TrustIfAuthVersionAsync(3, 4, It.Is<Device>(d =>
            d.ExpiresAt == Now.UtcDateTime.AddDays(90))), Times.Once);
    }

    [Fact]
    public async Task IsTrustedAsync_ShouldResendTheCookie_OnlyForTheTrustThatIsLeft()
    {
        var (service, deviceRepository, httpContext) = CreateWithPresentedDevice(new Device
        {
            Id = 11,
            UserId = 3,
            DeviceTokenHash = CryptoHelper.HashToken("device-id-abc"),
            DeviceType = "Desktop",
            ClientName = "Chrome",
            ExpiresAt = Now.UtcDateTime.AddDays(30),
        });

        var trusted = await service.IsTrustedAsync(3, TestRequestInfoFactory.Browser());

        trusted.Should().BeTrue();
        deviceRepository.Verify(r => r.UpdateLastSeenAsync(It.Is<Device>(d => d.Id == 11)), Times.Once);
        deviceRepository.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        httpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain($"max-age={(int)TimeSpan.FromDays(30).TotalSeconds}");
    }

    /// <summary>
    /// The cookie can outlive the trust, since keeping it or not is up to the client.
    /// </summary>
    [Fact]
    public async Task IsTrustedAsync_ShouldRefuseAnExpiredDevice_RemoveIt_AndClearTheCookie()
    {
        var events = new RecordingAuthSecurityEventEmitter();
        var (service, deviceRepository, httpContext) = CreateWithPresentedDevice(new Device
        {
            Id = 11,
            UserId = 3,
            DeviceTokenHash = CryptoHelper.HashToken("device-id-abc"),
            DeviceType = "Desktop",
            ClientName = "Chrome",
            ExpiresAt = Now.UtcDateTime,
        }, events);
        deviceRepository.Setup(r => r.DeleteAsync(3, 11)).ReturnsAsync(true);

        var trusted = await service.IsTrustedAsync(3, TestRequestInfoFactory.Browser());

        trusted.Should().BeFalse();
        deviceRepository.Verify(r => r.DeleteAsync(3, 11), Times.Once);
        events.Of(AuthSecurityEvents.TrustedDeviceExpired).Should().ContainSingle().Which.UserId.Should().Be(3);
        deviceRepository.Verify(r => r.UpdateLastSeenAsync(It.IsAny<Device>()), Times.Never);
        httpContext.Response.Headers.ContainsKey(HttpUtility.TrustedDeviceHeaderName).Should().BeFalse();
        httpContext.Response.Headers.SetCookie.ToString()
            .Should().Contain($"{HttpUtility.TrustedDeviceCookieName}=;")
            .And.Contain("expires=Thu, 01 Jan 1970");
    }

    [Fact]
    public async Task IsTrustedAsync_ShouldClearTheCookie_OfAnUnknownDevice()
    {
        var (service, deviceRepository, httpContext) = CreateWithPresentedDevice(device: null);

        var trusted = await service.IsTrustedAsync(3, TestRequestInfoFactory.Browser());

        trusted.Should().BeFalse();
        deviceRepository.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        httpContext.Response.Headers.SetCookie.ToString().Should().Contain($"{HttpUtility.TrustedDeviceCookieName}=;");
    }

    private static (DeviceTrustService Service, Mock<IDeviceRepository> Repository, DefaultHttpContext Context)
        CreateWithPresentedDevice(Device? device, IAuthSecurityEventEmitter? events = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HttpUtility.TrustedDeviceHeaderName] = "device-id-abc";
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns(httpContext);
        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository
            .Setup(r => r.GetDeviceAsync(3, CryptoHelper.HashToken("device-id-abc")))
            .ReturnsAsync(device);

        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser(),
            new FixedClock(Now),
            events);
        return (service, deviceRepository, httpContext);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
