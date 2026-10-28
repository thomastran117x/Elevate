using backend.main.features.auth.history;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class SecurityHistoryRetentionRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_ShouldDeleteOnlyExpiredEntries_AndCountThem()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var now = Now.UtcDateTime;
        await harness.SeedAsync(
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, now.AddDays(-91), expiresAt: now.AddDays(-1)),
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OtherId, now.AddDays(-90), expiresAt: now),
            SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, now.AddDays(-89), expiresAt: now.AddDays(1)));
        var (metrics, collector) = Metrics();
        using var _ = collector;

        var purged = await Runner(harness, metrics: metrics).RunOnceAsync();

        purged.Should().Be(2);
        (await harness.Db.AccountSecurityEvents.AsNoTracking().Select(e => e.ExpiresAtUtc).ToListAsync())
            .Should().Equal(now.AddDays(1));
        collector.Measurements.Should().ContainSingle(m => m.Instrument == "security_history.purged" && m.Value == 2);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStopAtTheBatchBudget()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var now = Now.UtcDateTime;
        await harness.SeedAsync(Enumerable.Range(1, 5)
            .Select(day => SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, now.AddDays(-100 - day), expiresAt: now.AddDays(-day)))
            .ToArray());

        var purged = await Runner(harness, Options(purgeBatchSize: 2, purgeMaxBatches: 2)).RunOnceAsync();

        // Oldest first, two batches of two; the fifth waits for the next pass.
        purged.Should().Be(4);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldDoNothing_WhenNothingHasExpired()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        await harness.SeedAsync(SecurityHistoryDbHarness.Entry(SecurityHistoryDbHarness.OwnerId, Now.UtcDateTime));
        var (metrics, collector) = Metrics();
        using var _ = collector;

        var purged = await Runner(harness, metrics: metrics).RunOnceAsync();

        purged.Should().Be(0);
        collector.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStop_WhenCancelled()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        await harness.SeedAsync(SecurityHistoryDbHarness.Entry(
            SecurityHistoryDbHarness.OwnerId,
            Now.UtcDateTime.AddDays(-100),
            expiresAt: Now.UtcDateTime.AddDays(-10)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var purged = await Runner(harness).RunOnceAsync(cancelled.Token);

        purged.Should().Be(0);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldRemoveOutboxRowsOlderThanADay()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var now = Now.UtcDateTime;
        harness.Db.SecurityHistoryOutbox.AddRange(
            new SecurityHistoryOutbox { AggregateType = "security-history", AggregateId = "1", Type = "record", CreatedAt = now.AddDays(-2) },
            new SecurityHistoryOutbox { AggregateType = "security-history", AggregateId = "1", Type = "record", CreatedAt = now.AddHours(-1) });
        await harness.Db.SaveChangesAsync();

        await Runner(harness).RunOnceAsync();

        (await harness.Db.SecurityHistoryOutbox.AsNoTracking().Select(o => o.CreatedAt).ToListAsync())
            .Should().Equal(now.AddHours(-1));
    }

    private static SecurityHistoryRetentionRunner Runner(
        SecurityHistoryDbHarness harness,
        SecurityHistoryWorkerOptions? options = null,
        SecurityHistoryWorkerMetrics? metrics = null) =>
        new(harness.Db, options ?? Options(), new HistoryClock(Now), metrics ?? Metrics().Metrics);
}
