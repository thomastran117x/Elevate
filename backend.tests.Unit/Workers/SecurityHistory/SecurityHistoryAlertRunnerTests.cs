using backend.main.features.auth.history;
using backend.main.features.auth.notifications;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryAlertRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_ShouldSendADueAlert_WithACancellableTimeout_AndMarkItSent()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        var notifications = new Mock<IAuthNotificationService>();
        CancellationToken sendToken = default;
        notifications
            .Setup(n => n.SendSecurityAlertAsync("owner@test.local", It.IsAny<AccountSecurityEvent>(), "Owner", It.IsAny<CancellationToken>()))
            .Callback<string, AccountSecurityEvent, string?, CancellationToken>((_, _, _, token) => sendToken = token)
            .Returns(Task.CompletedTask);
        var (metrics, collector) = Metrics();
        using var _ = collector;

        var handled = await Runner(harness, notifications.Object, metrics).RunOnceAsync();

        handled.Should().Be(1);
        // A bounded, cancellable send: an unavailable email topic cannot stall the dispatcher.
        sendToken.CanBeCanceled.Should().BeTrue();
        var stored = await Reload(harness, entry.Id);
        stored.AlertState.Should().Be(SecurityAlertState.Sent);
        stored.AlertAttempts.Should().Be(1);
        stored.AlertNextAttemptAtUtc.Should().BeNull();
        collector.Measurements.Should().ContainSingle(m => m.Instrument == "security_history.alerts" && m.Outcome == "sent");

        // Sent alerts are never picked up again.
        (await Runner(harness, notifications.Object).RunOnceAsync()).Should().Be(0);
        notifications.Verify(
            n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<AccountSecurityEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLeaveAlertsThatAreNotDueYet()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId, nextAttemptAt: Now.UtcDateTime.AddMinutes(5));
        var notifications = new Mock<IAuthNotificationService>();

        (await Runner(harness, notifications.Object).RunOnceAsync()).Should().Be(0);

        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldBackOffAfterAFailedSend()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        var (metrics, collector) = Metrics();
        using var _ = collector;

        await Runner(harness, Failing(), metrics).RunOnceAsync();

        var stored = await Reload(harness, entry.Id);
        stored.AlertState.Should().Be(SecurityAlertState.Pending);
        stored.AlertAttempts.Should().Be(1);
        stored.AlertNextAttemptAtUtc.Should().Be(Now.UtcDateTime + SecurityHistoryAlertRunner.RetryDelay(1));
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "retried");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldGiveUpAfterTheLastAttempt()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(
            harness,
            SecurityHistoryDbHarness.OwnerId,
            attempts: SecurityHistoryAlertRunner.MaxAttempts - 1);
        var (metrics, collector) = Metrics();
        using var _ = collector;

        await Runner(harness, Failing(), metrics).RunOnceAsync();

        var stored = await Reload(harness, entry.Id);
        stored.AlertState.Should().Be(SecurityAlertState.Failed);
        stored.AlertAttempts.Should().Be(SecurityHistoryAlertRunner.MaxAttempts);
        stored.AlertNextAttemptAtUtc.Should().BeNull();
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "failed");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStopTrying_WhenTheAccountHasNoAddress()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        await harness.Db.Users.Where(u => u.Id == SecurityHistoryDbHarness.OwnerId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, string.Empty));
        var notifications = new Mock<IAuthNotificationService>();
        var (metrics, collector) = Metrics();
        using var _ = collector;

        await Runner(harness, notifications.Object, metrics).RunOnceAsync();

        (await Reload(harness, entry.Id)).AlertState.Should().Be(SecurityAlertState.Failed);
        notifications.VerifyNoOtherCalls();
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "no_recipient");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLeaveTheAlertPending_WhenShuttingDownMidSend()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        using var stopping = new CancellationTokenSource();
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<AccountSecurityEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, AccountSecurityEvent, string?, CancellationToken>((_, _, _, token) =>
            {
                stopping.Cancel();
                return Task.FromCanceled(token);
            });

        var run = () => Runner(harness, notifications.Object).RunOnceAsync(stopping.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        var stored = await Reload(harness, entry.Id);
        stored.AlertState.Should().Be(SecurityAlertState.Pending);
        // Claimed, so it waits out the lease before another pass retries it.
        stored.AlertNextAttemptAtUtc.Should().Be(Now.UtcDateTime + SecurityHistoryAlertRunner.ClaimLease);
    }

    [Fact]
    public void RetryDelay_ShouldDoubleUpToTheCap()
    {
        SecurityHistoryAlertRunner.RetryDelay(1).Should().Be(TimeSpan.FromSeconds(30));
        SecurityHistoryAlertRunner.RetryDelay(2).Should().Be(TimeSpan.FromSeconds(60));
        SecurityHistoryAlertRunner.RetryDelay(20).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldTimeEachBackoffFromWhenThatAlertIsHandled()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var first = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        var second = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OtherId);
        var clock = new HistoryClock(Now);
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<AccountSecurityEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, AccountSecurityEvent, string?, CancellationToken>((_, entry, _, _) =>
            {
                // Slow sends: every alert in the batch is handled minutes after the pass began.
                clock.Now += TimeSpan.FromMinutes(5);
                return entry.Id == second.Id
                    ? Task.FromException(new InvalidOperationException("email topic unavailable"))
                    : Task.CompletedTask;
            });

        await Runner(harness, notifications.Object, clock: clock).RunOnceAsync();

        (await Reload(harness, first.Id)).AlertState.Should().Be(SecurityAlertState.Sent);
        // The backoff starts from when the send failed, not from when the pass began.
        (await Reload(harness, second.Id)).AlertNextAttemptAtUtc
            .Should().Be(Now.UtcDateTime.AddMinutes(10) + SecurityHistoryAlertRunner.RetryDelay(1));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldClaimEachAlertFromWhenItIsHandled()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var first = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        var second = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OtherId);
        var clock = new HistoryClock(Now);
        DateTime? secondLeaseSeenDuringSend = null;
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<AccountSecurityEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, AccountSecurityEvent, string?, CancellationToken>(async (_, entry, _, _) =>
            {
                if (entry.Id == first.Id)
                    clock.Now += TimeSpan.FromMinutes(2);
                else
                    secondLeaseSeenDuringSend = (await Reload(harness, second.Id)).AlertNextAttemptAtUtc;
            });

        await Runner(harness, notifications.Object, clock: clock).RunOnceAsync();

        // Claimed two minutes into the pass, so the lease still runs past "now" for other replicas.
        secondLeaseSeenDuringSend.Should().Be(Now.UtcDateTime.AddMinutes(2) + SecurityHistoryAlertRunner.ClaimLease);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldNotRetryASendThatTimedOut()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(harness, SecurityHistoryDbHarness.OwnerId);
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<AccountSecurityEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, AccountSecurityEvent, string?, CancellationToken>((_, _, _, token) =>
                Task.Delay(Timeout.Infinite, token));
        var (metrics, collector) = Metrics();
        using var _ = collector;
        var runner = new SecurityHistoryAlertRunner(
            harness.CreateContext(),
            notifications.Object,
            new HistoryClock(Now),
            Options(),
            metrics,
            TimeSpan.FromMilliseconds(50));

        await runner.RunOnceAsync();

        // The producer may still deliver it, so a retry could email the owner twice.
        var stored = await Reload(harness, entry.Id);
        stored.AlertState.Should().Be(SecurityAlertState.Unconfirmed);
        stored.AlertNextAttemptAtUtc.Should().BeNull();
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "unconfirmed");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldSkipAnAlertOlderThanTheAgeLimit()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var entry = await SeedPendingAsync(
            harness,
            SecurityHistoryDbHarness.OwnerId,
            occurredAt: Now.UtcDateTime - SecurityHistoryWorkerOptions.DefaultAlertMaxAge - TimeSpan.FromMinutes(1));
        var notifications = new Mock<IAuthNotificationService>();
        var (metrics, collector) = Metrics();
        using var _ = collector;

        await Runner(harness, notifications.Object, metrics).RunOnceAsync();

        (await Reload(harness, entry.Id)).AlertState.Should().Be(SecurityAlertState.Skipped);
        notifications.VerifyNoOtherCalls();
        collector.Measurements.Should().ContainSingle(m => m.Outcome == "skipped");
    }

    [Fact]
    public async Task Service_ShouldStayIdle_WhenAlertsAreOff()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var service = new SecurityHistoryAlertService(scopeFactory.Object, Options(notificationsEnabled: false));

        await service.StartAsync(CancellationToken.None);
        await (service.ExecuteTask ?? Task.CompletedTask);
        await service.StopAsync(CancellationToken.None);

        scopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    private static IAuthNotificationService Failing()
    {
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<AccountSecurityEvent>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("email topic unavailable"));
        return notifications.Object;
    }

    private static async Task<AccountSecurityEvent> SeedPendingAsync(
        SecurityHistoryDbHarness harness,
        int userId,
        DateTime? nextAttemptAt = null,
        int attempts = 0,
        DateTime? occurredAt = null)
    {
        var entry = SecurityHistoryDbHarness.Entry(userId, occurredAt ?? Now.UtcDateTime.AddMinutes(-1), AccountSecurityEventKind.PasswordChanged);
        entry.AlertState = SecurityAlertState.Pending;
        entry.AlertAttempts = attempts;
        entry.AlertNextAttemptAtUtc = nextAttemptAt ?? Now.UtcDateTime.AddSeconds(-1);
        await harness.SeedAsync(entry);
        return entry;
    }

    private static async Task<AccountSecurityEvent> Reload(SecurityHistoryDbHarness harness, long id)
    {
        await using var db = harness.CreateContext();
        return await db.AccountSecurityEvents.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private static SecurityHistoryAlertRunner Runner(
        SecurityHistoryDbHarness harness,
        IAuthNotificationService notifications,
        SecurityHistoryWorkerMetrics? metrics = null,
        TimeProvider? clock = null) =>
        new(
            harness.CreateContext(),
            notifications,
            clock ?? new HistoryClock(Now),
            Options(),
            metrics ?? Metrics().Metrics);
}
