using backend.main.features.auth.history;
using backend.main.features.auth.notifications;
using backend.main.shared.providers.messages;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using FluentAssertions;

using Moq;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryAlertSenderTests
{
    [Fact]
    public async Task SendIfDueAsync_ShouldEmailTheOwner_ForAnAlertKind()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        var (metrics, collector) = Metrics();
        using var _ = collector;
        var entry = Message(userId: SecurityHistoryDbHarness.OwnerId, kind: AccountSecurityEventKind.PasswordChanged);

        await Sender(harness, notifications.Object, metrics).SendIfDueAsync(entry);

        notifications.Verify(n => n.SendSecurityAlertAsync("owner@test.local", entry, "Owner"), Times.Once);
        collector.Measurements.Should().ContainSingle(m => m.Instrument == "security_history.alerts" && m.Outcome == "sent");
    }

    [Fact]
    public async Task SendIfDueAsync_ShouldStayQuiet_ForKindsThatAreNotAlerts()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();

        await Sender(harness, notifications.Object)
            .SendIfDueAsync(Message(userId: SecurityHistoryDbHarness.OwnerId, kind: AccountSecurityEventKind.SignedIn));

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendIfDueAsync_ShouldStayQuiet_WhenNotificationsAreOff()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();

        await Sender(harness, notifications.Object, options: Options(notificationsEnabled: false))
            .SendIfDueAsync(Message(userId: SecurityHistoryDbHarness.OwnerId));

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendIfDueAsync_ShouldSkip_WhenTheAccountHasNoAddress()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        var (metrics, collector) = Metrics();
        using var _ = collector;

        await Sender(harness, notifications.Object, metrics).SendIfDueAsync(Message(userId: 404));

        notifications.VerifyNoOtherCalls();
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "no_recipient");
    }

    [Fact]
    public async Task SendIfDueAsync_ShouldSwallowADeliveryFailure()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<SecurityHistoryMessage>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));
        var (metrics, collector) = Metrics();
        using var _ = collector;

        var send = () => Sender(harness, notifications.Object, metrics)
            .SendIfDueAsync(Message(userId: SecurityHistoryDbHarness.OwnerId));

        await send.Should().NotThrowAsync();
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "failed");
    }

    [Fact]
    public async Task SendIfDueAsync_ShouldPassCancellationThrough()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var send = () => Sender(harness, Mock.Of<IAuthNotificationService>())
            .SendIfDueAsync(Message(userId: SecurityHistoryDbHarness.OwnerId), cancelled.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
    }

    private static SecurityHistoryAlertSender Sender(
        SecurityHistoryDbHarness harness,
        IAuthNotificationService notifications,
        SecurityHistoryWorkerMetrics? metrics = null,
        SecurityHistoryWorkerOptions? options = null) =>
        new(harness.Db, notifications, options ?? Options(), metrics ?? Metrics().Metrics);
}
