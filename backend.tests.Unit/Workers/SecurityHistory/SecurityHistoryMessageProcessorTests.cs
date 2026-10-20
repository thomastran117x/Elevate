using System.Text.Json;

using backend.main.features.auth.history;
using backend.main.features.auth.notifications;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Moq;

using Polly;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldStoreTheEntryAndAlert_WhenItIsNew()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        var message = Message(userId: SecurityHistoryDbHarness.OwnerId);

        var outcome = await Processor(harness, notifications.Object, dlq.Object).ProcessAsync(Envelope(message));

        outcome.Should().Be(SecurityHistoryEntryOutcome.Inserted);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(1);
        notifications.Verify(n => n.SendSecurityAlertAsync("owner@test.local", It.IsAny<SecurityHistoryMessage>(), "Owner"), Times.Once);
        dlq.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldNeverAlertTwice_ForARedelivery()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        var message = Message(userId: SecurityHistoryDbHarness.OwnerId);
        var processor = Processor(harness, notifications.Object, Mock.Of<ISecurityHistoryWorkerDlqPublisher>());

        await processor.ProcessAsync(Envelope(message));
        var again = await processor.ProcessAsync(Envelope(message));

        again.Should().Be(SecurityHistoryEntryOutcome.Duplicate);
        notifications.Verify(
            n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<SecurityHistoryMessage>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldDropAnEntryForADeletedAccount_WithoutTheDlq()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();

        var outcome = await Processor(harness, notifications.Object, dlq.Object).ProcessAsync(Envelope(Message(userId: 404)));

        outcome.Should().Be(SecurityHistoryEntryOutcome.AccountDeleted);
        notifications.VerifyNoOtherCalls();
        dlq.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldSkipAnExpiredEntry()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();

        var outcome = await Processor(harness, notifications.Object, Mock.Of<ISecurityHistoryWorkerDlqPublisher>())
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId, occurredAt: Now.AddDays(-100))));

        outcome.Should().Be(SecurityHistoryEntryOutcome.Expired);
        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldSendAMalformedPayloadToTheDlq()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        var envelope = Envelope("{\"kind\":\"Nope\"}");

        var outcome = await Processor(harness, Mock.Of<IAuthNotificationService>(), dlq.Object).ProcessAsync(envelope);

        outcome.Should().Be(SecurityHistoryEntryOutcome.Rejected);
        dlq.Verify(d => d.PublishAsync(envelope, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRetryAndThenDeadLetter_WhenStoringKeepsFailing()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        // A closed connection makes every attempt fail the way an unreachable database would.
        await harness.Db.Database.CloseConnectionAsync();
        await harness.Db.Database.GetDbConnection().DisposeAsync();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        var attempts = 0;
        var retries = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.Zero,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                OnRetry = _ =>
                {
                    attempts++;
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
        var envelope = Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId));

        var outcome = await Processor(harness, Mock.Of<IAuthNotificationService>(), dlq.Object, retries).ProcessAsync(envelope);

        outcome.Should().Be(SecurityHistoryEntryOutcome.Failed);
        attempts.Should().Be(2);
        dlq.Verify(d => d.PublishAsync(envelope, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldKeepTheEntry_WhenTheAlertFails()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var notifications = new Mock<IAuthNotificationService>();
        notifications
            .Setup(n => n.SendSecurityAlertAsync(It.IsAny<string>(), It.IsAny<SecurityHistoryMessage>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("email topic unavailable"));
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();

        var outcome = await Processor(harness, notifications.Object, dlq.Object)
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId)));

        outcome.Should().Be(SecurityHistoryEntryOutcome.Inserted);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(1);
        dlq.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldPassCancellationThrough()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var process = () => Processor(harness, Mock.Of<IAuthNotificationService>(), Mock.Of<ISecurityHistoryWorkerDlqPublisher>())
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId)), cancelled.Token);

        await process.Should().ThrowAsync<OperationCanceledException>();
    }

    private static SecurityHistoryMessageProcessor Processor(
        SecurityHistoryDbHarness harness,
        IAuthNotificationService notifications,
        ISecurityHistoryWorkerDlqPublisher dlq,
        ResiliencePipeline? retries = null)
    {
        var options = Options();
        var metrics = Metrics().Metrics;
        var writer = new SecurityHistoryWriter(harness.Db, options, new HistoryClock(Now));
        var alerts = new SecurityHistoryAlertSender(harness.Db, notifications, options, metrics);
        return new SecurityHistoryMessageProcessor(writer, alerts, dlq, metrics, retries ?? ResiliencePipeline.Empty);
    }

    private static KafkaMessageEnvelope Envelope(SecurityHistoryMessage message) =>
        Envelope(JsonSerializer.Serialize(message, JsonOptions.Default));

    private static KafkaMessageEnvelope Envelope(string payload) =>
        new("eventxperience-security-history", 0, 42, null, payload, new Dictionary<string, string?>());
}
