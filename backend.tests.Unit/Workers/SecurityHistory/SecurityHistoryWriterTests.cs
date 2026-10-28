using backend.main.features.auth.history;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryWriterTests
{
    [Fact]
    public async Task InsertAsync_ShouldAppendTheEntry_WithItsRetentionDeadline()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var message = Message(userId: SecurityHistoryDbHarness.OwnerId, occurredAt: Now.AddHours(-2));

        var result = await Writer(harness, retentionDays: 30).InsertAsync(message);

        result.Should().Be(SecurityHistoryWriteResult.Inserted);
        var stored = await harness.Db.AccountSecurityEvents.AsNoTracking().SingleAsync();
        stored.MessageId.Should().Be(message.MessageId);
        stored.UserId.Should().Be(SecurityHistoryDbHarness.OwnerId);
        stored.Kind.Should().Be(AccountSecurityEventKind.PasswordChanged);
        stored.EventName.Should().Be(message.EventName);
        stored.Factor.Should().Be("password");
        stored.Client.Should().Be(SecurityHistoryClient.Chrome);
        stored.Network.Should().Be("203.0.113.0/24");
        stored.CorrelationId.Should().Be(message.CorrelationId);
        stored.OccurredAtUtc.Should().Be(Now.AddHours(-2).UtcDateTime);
        stored.RecordedAtUtc.Should().Be(Now.UtcDateTime);
        stored.ExpiresAtUtc.Should().Be(Now.AddHours(-2).AddDays(30).UtcDateTime);
    }

    [Fact]
    public async Task InsertAsync_ShouldTreatARedeliveryAsADuplicate()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var message = Message(userId: SecurityHistoryDbHarness.OwnerId);
        var writer = Writer(harness);

        await writer.InsertAsync(message);
        var again = await writer.InsertAsync(message);

        again.Should().Be(SecurityHistoryWriteResult.Duplicate);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_ShouldReportADuplicate_WhenAnotherConsumerWinsTheInsertRace()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var message = Message(userId: SecurityHistoryDbHarness.OwnerId);
        // The row lands between this writer's existence check and its insert.
        harness.Db.SavingChanges += (_, _) =>
        {
            using var other = harness.CreateContext();
            if (!other.AccountSecurityEvents.Any(e => e.MessageId == message.MessageId))
            {
                other.AccountSecurityEvents.Add(SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, Now.UtcDateTime));
                other.AccountSecurityEvents.Local.Single().MessageId = message.MessageId;
                other.SaveChanges();
            }
        };

        var result = await Writer(harness).InsertAsync(message);

        result.Should().Be(SecurityHistoryWriteResult.Duplicate);
    }

    [Fact]
    public async Task InsertAsync_ShouldDropTheEntry_WhenTheAccountWasDeleted()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();

        var result = await Writer(harness).InsertAsync(Message(userId: 404));

        result.Should().Be(SecurityHistoryWriteResult.AccountDeleted);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InsertAsync_ShouldSkipAnEntryAlreadyPastRetention()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();

        var result = await Writer(harness, retentionDays: 90)
            .InsertAsync(Message(userId: SecurityHistoryDbHarness.OwnerId, occurredAt: Now.AddDays(-91)));

        result.Should().Be(SecurityHistoryWriteResult.Expired);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(AccountSecurityEventKind.PasswordChanged, true, SecurityAlertState.Pending)]
    [InlineData(AccountSecurityEventKind.SignedIn, true, SecurityAlertState.None)]
    [InlineData(AccountSecurityEventKind.PasswordChanged, false, SecurityAlertState.None)]
    public async Task InsertAsync_ShouldStoreWhetherTheEntryOwesAnAlert(
        AccountSecurityEventKind kind,
        bool notificationsEnabled,
        SecurityAlertState expected)
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var writer = new SecurityHistoryWriter(
            harness.Db,
            Options(notificationsEnabled: notificationsEnabled),
            new HistoryClock(Now));

        await writer.InsertAsync(Message(userId: SecurityHistoryDbHarness.OwnerId, kind: kind));

        var stored = await harness.Db.AccountSecurityEvents.AsNoTracking().SingleAsync();
        stored.AlertState.Should().Be(expected);
        stored.AlertAttempts.Should().Be(0);
        stored.AlertNextAttemptAtUtc.Should().Be(expected == SecurityAlertState.Pending ? Now.UtcDateTime : null);
    }

    private static SecurityHistoryWriter Writer(SecurityHistoryDbHarness harness, int retentionDays = 90) =>
        new(harness.Db, Options(retentionDays: retentionDays), new HistoryClock(Now));
}
