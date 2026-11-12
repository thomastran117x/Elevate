using backend.main.features.auth.device;
using backend.main.features.auth.events;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities;

using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class TrustedDeviceManagementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_ShouldMarkThePresentedDevice_AndNeverExposeFreeTextOrFullAddresses()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.ListActiveAsync(7, Now.UtcDateTime)).ReturnsAsync(
        [
            NewDevice(1, "laptop-token", clientName: "Chrome", deviceType: "Desktop", ipAddress: "198.51.100.77"),
            NewDevice(2, "phone-token", clientName: "SomeBot <img src=x>", deviceType: "Mobile", ipAddress: "Unknown"),
        ]);
        var service = CreateService(devices, new RecordingAuthSecurityEventEmitter());

        var result = await service.ListAsync(7, "phone-token");

        result.Devices.Should().HaveCount(2);
        var laptop = result.Devices[0];
        laptop.Current.Should().BeFalse();
        laptop.Client.Should().Be("chrome");
        laptop.DeviceClass.Should().Be("desktop");
        laptop.Network.Should().Be("198.51.100.0/24");
        laptop.ExpiresAt.Should().Be(Now.UtcDateTime.AddDays(30));
        var phone = result.Devices[1];
        phone.Current.Should().BeTrue();
        phone.Client.Should().Be("other");
        phone.DeviceClass.Should().Be("mobile");
        phone.Network.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ShouldMarkNothingCurrent_WhenNoDeviceTokenWasPresented()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.ListActiveAsync(7, Now.UtcDateTime)).ReturnsAsync([NewDevice(1, "laptop-token")]);
        var service = CreateService(devices, new RecordingAuthSecurityEventEmitter());

        var result = await service.ListAsync(7, currentDeviceToken: null);

        result.Devices.Should().ContainSingle().Which.Current.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_ShouldForgetTheDevice_AndRecordIt()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.GetDeviceAsync(7, CryptoHelper.HashToken("laptop-token")))
            .ReturnsAsync(NewDevice(1, "laptop-token"));
        devices.Setup(d => d.DeleteAsync(7, 1)).ReturnsAsync(true);
        var events = new RecordingAuthSecurityEventEmitter();
        var service = CreateService(devices, events);

        var result = await service.RevokeAsync(7, 1, "laptop-token");

        result.CurrentDeviceRevoked.Should().BeTrue();
        result.RevokedCount.Should().Be(1);
        var recorded = events.Of(AuthSecurityEvents.TrustedDeviceRevoked).Should().ContainSingle().Subject;
        recorded.UserId.Should().Be(7);
        recorded.Reason.Should().Be(AuthSecurityReason.UserRevokedTrustedDevice);
    }

    [Fact]
    public async Task RevokeAsync_ShouldBeNotFound_ForADeviceTheCallerDoesNotOwn_AndRecordNothing()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.DeleteAsync(7, 99)).ReturnsAsync(false);
        var events = new RecordingAuthSecurityEventEmitter();
        var service = CreateService(devices, events);

        var act = () => service.RevokeAsync(7, 99, currentDeviceToken: null);

        await act.Should().ThrowAsync<ResourceNotFoundException>();
        events.Emitted.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeAsync_ShouldNotReportTheCurrentDevice_WhenAnotherWasForgotten()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.GetDeviceAsync(7, CryptoHelper.HashToken("laptop-token")))
            .ReturnsAsync(NewDevice(1, "laptop-token"));
        devices.Setup(d => d.DeleteAsync(7, 2)).ReturnsAsync(true);
        var service = CreateService(devices, new RecordingAuthSecurityEventEmitter());

        var result = await service.RevokeAsync(7, 2, "laptop-token");

        result.CurrentDeviceRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldForgetEveryDevice_AndRecordItOnce()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.GetDeviceAsync(7, CryptoHelper.HashToken("laptop-token")))
            .ReturnsAsync(NewDevice(1, "laptop-token"));
        devices.Setup(d => d.DeleteAllAsync(7)).ReturnsAsync(3);
        var events = new RecordingAuthSecurityEventEmitter();
        var service = CreateService(devices, events);

        var result = await service.RevokeAllAsync(7, "laptop-token");

        result.RevokedCount.Should().Be(3);
        result.CurrentDeviceRevoked.Should().BeTrue();
        events.Of(AuthSecurityEvents.TrustedDeviceRevoked).Should().ContainSingle()
            .Which.Reason.Should().Be(AuthSecurityReason.UserRevokedAllTrustedDevices);
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldRecordNothing_WhenThereWasNothingToForget()
    {
        var devices = new Mock<IDeviceRepository>();
        devices.Setup(d => d.DeleteAllAsync(7)).ReturnsAsync(0);
        var events = new RecordingAuthSecurityEventEmitter();
        var service = CreateService(devices, events);

        var result = await service.RevokeAllAsync(7, currentDeviceToken: null);

        result.RevokedCount.Should().Be(0);
        result.CurrentDeviceRevoked.Should().BeFalse();
        events.Emitted.Should().BeEmpty();
    }

    private static TrustedDeviceManagementService CreateService(
        Mock<IDeviceRepository> devices,
        RecordingAuthSecurityEventEmitter events) =>
        new(devices.Object, events, new FixedClock(Now));

    private static Device NewDevice(
        int id,
        string token,
        string clientName = "Chrome",
        string deviceType = "Desktop",
        string ipAddress = "127.0.0.1") => new()
        {
            Id = id,
            UserId = 7,
            DeviceTokenHash = CryptoHelper.HashToken(token),
            ClientName = clientName,
            DeviceType = deviceType,
            IpAddress = ipAddress,
            ExpiresAt = Now.UtcDateTime.AddDays(30),
        };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
