using backend.main.features.auth.device;
using backend.main.shared.requests;

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
    public async Task TrustAsync_ShouldCreateDevice_WhenNoPriorDeviceExists()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository.Setup(r => r.GetDeviceAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((Device?)null);

        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        await service.TrustAsync(3, "device-id-abc", "Desktop", "Chrome", "127.0.0.1");

        deviceRepository.Verify(r => r.CreateDeviceAsync(It.Is<Device>(d =>
            d.UserId == 3
            && d.DeviceType == "Desktop"
            && d.ClientName == "Chrome"
            && d.IpAddress == "127.0.0.1")), Times.Once);
    }

    [Fact]
    public async Task TrustAsync_ShouldUpdateExistingDevice_WhenDeviceAlreadyTrusted()
    {
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var existingDevice = new Device
        {
            UserId = 3,
            DeviceTokenHash = "existing-hash",
            DeviceType = "Mobile",
            ClientName = "Safari",
            IpAddress = "10.0.0.1"
        };

        var deviceRepository = new Mock<IDeviceRepository>();
        deviceRepository.Setup(r => r.GetDeviceAsync(It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(existingDevice);

        var service = new DeviceTrustService(
            deviceRepository.Object,
            httpContextAccessor.Object,
            TestRequestInfoFactory.Browser());

        await service.TrustAsync(3, "device-id-abc", "Desktop", "Chrome", "127.0.0.1");

        deviceRepository.Verify(r => r.UpdateLastSeenAsync(
            It.Is<Device>(d => d.DeviceType == "Desktop" && d.ClientName == "Chrome")), Times.Once);
        deviceRepository.Verify(r => r.CreateDeviceAsync(It.IsAny<Device>()), Times.Never);
    }
}
