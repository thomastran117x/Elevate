using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.features.auth.sessions;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.Sessions;

public class SessionManagementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string Current = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Other = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task ListAsync_ShouldMarkTheCurrentSession_AndUseWireNames()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        var current = AuthSessionDbHarness.NewSession(7, Current, Now.UtcDateTime, Now.UtcDateTime.AddDays(1));
        current.Client = SecurityHistoryClient.ApiClient;
        current.DeviceClass = AuthDeviceClass.Desktop;
        current.Network = "203.0.113.0/24";
        current.RememberMe = true;
        var other = AuthSessionDbHarness.NewSession(7, Other, Now.UtcDateTime.AddHours(-2), Now.UtcDateTime.AddDays(1));
        other.Transport = SessionTransport.ApiToken;
        sessions.Setup(s => s.ListActiveAsync(7, Now.UtcDateTime)).ReturnsAsync([current, other]);
        var service = CreateService(sessions, new Mock<ITokenService>());

        var result = await service.ListAsync(7, Current);

        result.Sessions.Should().HaveCount(2);
        var first = result.Sessions[0];
        first.Id.Should().Be(Current);
        first.Current.Should().BeTrue();
        first.Transport.Should().Be("browser");
        first.Client.Should().Be("api_client");
        first.DeviceClass.Should().Be("desktop");
        first.Network.Should().Be("203.0.113.0/24");
        first.RememberMe.Should().BeTrue();
        first.ExpiresAt.Should().Be(Now.UtcDateTime.AddDays(1));
        result.Sessions[1].Current.Should().BeFalse();
        result.Sessions[1].Transport.Should().Be("api");
        result.Sessions[1].Client.Should().Be("unknown");
    }

    [Fact]
    public async Task RevokeAsync_ShouldReportWhetherItWasTheCurrentSession()
    {
        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.RevokeUserRefreshSessionAsync(7, It.IsAny<string>(), AuthSecurityReason.UserRevokedSession))
            .ReturnsAsync(true);
        var service = CreateService(new Mock<IAuthSessionRepository>(), tokens);

        (await service.RevokeAsync(7, Current, Other)).CurrentSessionRevoked.Should().BeFalse();
        var self = await service.RevokeAsync(7, Current, Current);

        self.CurrentSessionRevoked.Should().BeTrue();
        self.RevokedCount.Should().Be(1);
    }

    [Fact]
    public async Task RevokeAsync_ShouldBeNotFound_ForASessionTheCallerDoesNotOwn()
    {
        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.RevokeUserRefreshSessionAsync(7, Other, AuthSecurityReason.UserRevokedSession))
            .ReturnsAsync(false);
        var service = CreateService(new Mock<IAuthSessionRepository>(), tokens);

        var act = () => service.RevokeAsync(7, Current, Other);

        await act.Should().ThrowAsync<ResourceNotFoundException>();
    }

    [Fact]
    public async Task RevokeOthersAsync_ShouldKeepTheCurrentSession()
    {
        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.RevokeOtherRefreshSessionsAsync(7, Current, AuthSecurityReason.UserRevokedOtherSessions))
            .ReturnsAsync(3);
        var service = CreateService(new Mock<IAuthSessionRepository>(), tokens);

        var result = await service.RevokeOthersAsync(7, Current);

        result.RevokedCount.Should().Be(3);
        result.CurrentSessionRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAllAsync_ShouldEndEverySession_TheCurrentOneIncluded()
    {
        var sessions = new Mock<IAuthSessionRepository>();
        sessions.Setup(s => s.ListActiveAsync(7, Now.UtcDateTime)).ReturnsAsync(
        [
            AuthSessionDbHarness.NewSession(7, Current, Now.UtcDateTime, Now.UtcDateTime.AddDays(1)),
            AuthSessionDbHarness.NewSession(7, Other, Now.UtcDateTime, Now.UtcDateTime.AddDays(1)),
        ]);
        var tokens = new Mock<ITokenService>();
        var service = CreateService(sessions, tokens);

        var result = await service.RevokeAllAsync(7);

        tokens.Verify(t => t.RevokeAllRefreshSessionsAsync(7, AuthSecurityReason.UserRevokedAllSessions, null), Times.Once);
        result.RevokedCount.Should().Be(2);
        result.CurrentSessionRevoked.Should().BeTrue();
    }

    private static SessionManagementService CreateService(
        Mock<IAuthSessionRepository> sessions,
        Mock<ITokenService> tokens) =>
        new(sessions.Object, tokens.Object, new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
