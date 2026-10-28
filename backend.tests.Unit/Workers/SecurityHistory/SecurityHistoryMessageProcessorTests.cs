using System.Text.Json;

using backend.main.features.auth.history;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Moq;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldStoreTheEntry_WithItsAlertPending()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();

        var outcome = await Processor(harness, dlq.Object)
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId)));

        outcome.Should().Be(SecurityHistoryEntryOutcome.Inserted);
        var stored = await harness.Db.AccountSecurityEvents.AsNoTracking().SingleAsync();
        // The alert travels with the row and is sent by the dispatcher, never inline.
        stored.AlertState.Should().Be(SecurityAlertState.Pending);
        dlq.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldLeaveARedeliveredEntrysAlertAsItWas()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var message = Message(userId: SecurityHistoryDbHarness.OwnerId);
        var processor = Processor(harness, Mock.Of<ISecurityHistoryWorkerDlqPublisher>());

        await processor.ProcessAsync(Envelope(message));
        var again = await processor.ProcessAsync(Envelope(message));

        again.Should().Be(SecurityHistoryEntryOutcome.Duplicate);
        var stored = await harness.Db.AccountSecurityEvents.AsNoTracking().SingleAsync();
        stored.AlertState.Should().Be(SecurityAlertState.Pending);
    }

    [Fact]
    public async Task ProcessAsync_ShouldDropAnEntryForADeletedAccount_WithoutTheDlq()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();

        var outcome = await Processor(harness, dlq.Object).ProcessAsync(Envelope(Message(userId: 404)));

        outcome.Should().Be(SecurityHistoryEntryOutcome.AccountDeleted);
        dlq.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ShouldSkipAnExpiredEntry()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();

        var outcome = await Processor(harness, Mock.Of<ISecurityHistoryWorkerDlqPublisher>())
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId, occurredAt: Now.AddDays(-100))));

        outcome.Should().Be(SecurityHistoryEntryOutcome.Expired);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_ShouldSendAMalformedPayloadToTheDlq()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        var envelope = Envelope("{\"kind\":\"Nope\"}");

        var outcome = await Processor(harness, dlq.Object).ProcessAsync(envelope);

        outcome.Should().Be(SecurityHistoryEntryOutcome.Rejected);
        dlq.Verify(d => d.PublishAsync(envelope, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldLetAStorageFailurePropagate_SoTheMessageIsRetried()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        // A closed connection fails the insert the way an unreachable database would.
        await harness.Db.Database.CloseConnectionAsync();
        await harness.Db.Database.GetDbConnection().DisposeAsync();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        var (metrics, collector) = Metrics();
        using var _ = collector;

        var process = () => Processor(harness, dlq.Object, metrics)
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId)));

        // The consumer leaves the offset uncommitted and tries again; an outage is not a dead letter.
        await process.Should().ThrowAsync<Exception>();
        dlq.VerifyNoOtherCalls();
        collector.Measurements.Should().ContainSingle(m => m.Instrument == "security_history.entries" && m.Outcome == "failed");
    }

    [Fact]
    public async Task ProcessAsync_ShouldPassCancellationThrough()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var process = () => Processor(harness, Mock.Of<ISecurityHistoryWorkerDlqPublisher>())
            .ProcessAsync(Envelope(Message(userId: SecurityHistoryDbHarness.OwnerId)), cancelled.Token);

        await process.Should().ThrowAsync<OperationCanceledException>();
    }

    private static SecurityHistoryMessageProcessor Processor(
        SecurityHistoryDbHarness harness,
        ISecurityHistoryWorkerDlqPublisher dlq,
        SecurityHistoryWorkerMetrics? metrics = null) =>
        new(
            new SecurityHistoryWriter(harness.Db, Options(), new HistoryClock(Now)),
            dlq,
            metrics ?? Metrics().Metrics);

    private static KafkaMessageEnvelope Envelope(SecurityHistoryMessage message) =>
        Envelope(JsonSerializer.Serialize(message, JsonOptions.Default));

    private static KafkaMessageEnvelope Envelope(string payload) =>
        new("eventxperience-security-history", 0, 42, null, payload, new Dictionary<string, string?>());
}
