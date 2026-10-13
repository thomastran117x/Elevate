using backend.main.features.auth.device;
using backend.main.shared.requests;
using backend.main.shared.utilities;
using backend.main.utilities;

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
}
