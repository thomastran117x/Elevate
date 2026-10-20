using backend.main.features.auth.events;
using backend.main.features.auth.history;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPageAsync_ShouldQueryAtTheCurrentTime_AndMapToWireNames()
    {
        var entry = new AccountSecurityEvent
        {
            Id = 42,
            UserId = 7,
            Kind = AccountSecurityEventKind.RefreshTokenReuseDetected,
            EventName = AuthSecurityEvents.RefreshTokenReused.Name,
            Method = "refresh_token",
            Reason = "token_reuse",
            Client = SecurityHistoryClient.ApiClient,
            DeviceClass = AuthDeviceClass.ApiClient,
            Network = "2001:db8:abcd::/48",
            CorrelationId = "req-42",
            OccurredAtUtc = Now.UtcDateTime.AddMinutes(-5),
        };
        var repository = new Mock<ISecurityHistoryRepository>();
        repository
            .Setup(r => r.GetPageAsync(7, Now.UtcDateTime, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([entry], 11));

        var service = new SecurityHistoryService(repository.Object, new HistoryClock(Now));
        var page = await service.GetPageAsync(7, 2, 10);

        page.TotalCount.Should().Be(11);
        page.TotalPages.Should().Be(2);
        page.Page.Should().Be(2);
        var item = page.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(42);
        item.Kind.Should().Be("refresh_token_reuse_detected");
        item.Method.Should().Be("refresh_token");
        item.Reason.Should().Be("token_reuse");
        item.Client.Should().Be("api_client");
        item.DeviceClass.Should().Be("api_client");
        item.Network.Should().Be("2001:db8:abcd::/48");
        item.InitiatedByAdmin.Should().BeFalse();
        item.Reference.Should().Be("req-42");
        item.OccurredAt.Should().Be(entry.OccurredAtUtc);
    }
}
