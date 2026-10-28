using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.shared.providers.messages;
using backend.tests.Unit.Features.Auth.Events;
using backend.worker.security_history_worker;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryOutboxTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainAllAsync_ShouldStoreEveryQueuedEntry_AsAPayloadTheWorkerAccepts()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var queue = new SecurityHistoryQueue();
        var first = Message(SecurityHistoryDbHarness.OwnerId);
        var second = Message(SecurityHistoryDbHarness.OtherId);
        queue.TryEnqueue(first).Should().BeTrue();
        queue.TryEnqueue(second).Should().BeTrue();

        var stored = await Drainer(harness, queue).DrainAllAsync();

        stored.Should().Be(2);
        var rows = await harness.Db.SecurityHistoryOutbox.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(r =>
            r.AggregateType == SecurityHistoryOutboxWriter.AggregateType
            && r.Type == SecurityHistoryOutboxWriter.RecordType);
        rows.Select(r => r.AggregateId).Should().BeEquivalentTo(["1", "2"]);
        // Debezium relays the payload as the message value, so it must be what the worker parses.
        var relayed = rows.Select(r => SecurityHistoryMessageParser.Parse(r.Payload)).ToList();
        relayed.Select(m => m.MessageId).Should().BeEquivalentTo([first.MessageId, second.MessageId]);
        relayed.Single(m => m.MessageId == first.MessageId).Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task DrainOnceAsync_ShouldKeepAFailedBatch_AndStoreItOnTheNextCall()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var queue = new SecurityHistoryQueue();
        var message = Message(SecurityHistoryDbHarness.OwnerId);
        queue.TryEnqueue(message);
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var failing = true;
        var drainer = Drainer(harness, queue, new AuthSecurityMetrics(meterFactory), () =>
        {
            if (failing)
                throw new InvalidOperationException("database unavailable");
        });

        var first = () => drainer.DrainOnceAsync();

        await first.Should().ThrowAsync<InvalidOperationException>();
        recorder.Measurements.Should().ContainSingle(m => m.Instrument == "auth.security_history.store_failures");
        (await harness.Db.SecurityHistoryOutbox.CountAsync()).Should().Be(0);

        // The batch was taken off the queue but not lost: the next call stores it.
        failing = false;
        (await drainer.DrainOnceAsync()).Should().Be(1);
        var payload = (await harness.Db.SecurityHistoryOutbox.AsNoTracking().SingleAsync()).Payload;
        SecurityHistoryMessageParser.Parse(payload).MessageId.Should().Be(message.MessageId);
    }

    [Fact]
    public async Task DrainOnceAsync_ShouldDoNothing_WhenNothingIsQueued()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();

        (await Drainer(harness, new SecurityHistoryQueue()).DrainOnceAsync()).Should().Be(0);
    }

    [Fact]
    public void TakeBatch_ShouldStopAtTheRequestedSize()
    {
        var queue = new SecurityHistoryQueue();
        for (var i = 0; i < 5; i++)
            queue.TryEnqueue(Message(1));

        queue.TakeBatch(3).Should().HaveCount(3);
        queue.TakeBatch(10).Should().HaveCount(2);
    }

    [Fact]
    public async Task Service_ShouldStoreEntriesAsTheyArrive_AndFlushTheQueueOnStop()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var queue = new SecurityHistoryQueue();
        IHostedService service = new SecurityHistoryOutboxService(queue, Drainer(harness, queue));

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(Message(SecurityHistoryDbHarness.OwnerId));
        await WaitForOutboxRowsAsync(harness, 1);

        queue.TryEnqueue(Message(SecurityHistoryDbHarness.OtherId));
        await service.StopAsync(CancellationToken.None);

        (await harness.Db.SecurityHistoryOutbox.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Service_ShouldRetryAfterAFailedWrite()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        var queue = new SecurityHistoryQueue();
        var attempts = 0;
        var drainer = Drainer(harness, queue, beforeWrite: () =>
        {
            if (++attempts == 1)
                throw new InvalidOperationException("database unavailable");
        });
        IHostedService service = new SecurityHistoryOutboxService(queue, drainer);

        await service.StartAsync(CancellationToken.None);
        queue.TryEnqueue(Message(SecurityHistoryDbHarness.OwnerId));
        await WaitForOutboxRowsAsync(harness, 1, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        attempts.Should().BeGreaterThanOrEqualTo(2);
    }

    private static async Task WaitForOutboxRowsAsync(SecurityHistoryDbHarness harness, int count, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            await using var db = harness.CreateContext();
            if (await db.SecurityHistoryOutbox.CountAsync() >= count)
                return;
            await Task.Delay(25);
        }

        throw new TimeoutException($"Expected {count} outbox rows.");
    }

    private static SecurityHistoryOutboxDrainer Drainer(
        SecurityHistoryDbHarness harness,
        SecurityHistoryQueue queue,
        AuthSecurityMetrics? metrics = null,
        Action? beforeWrite = null)
    {
        var services = new ServiceCollection();
        // A fresh context per scope, as in the API, all on the harness's in-memory database.
        services.AddScoped(_ =>
        {
            var db = harness.CreateContext();
            if (beforeWrite is not null)
                db.SavingChanges += (_, _) => beforeWrite();
            return db;
        });
        services.AddScoped<SecurityHistoryOutboxWriter>();
        return new SecurityHistoryOutboxDrainer(
            queue,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            metrics ?? new AuthSecurityMetrics(AuthSecurityEventTestSupport.CreateMeterFactory()));
    }

    private static SecurityHistoryMessage Message(int userId) => new()
    {
        MessageId = Guid.NewGuid(),
        UserId = userId,
        Kind = AccountSecurityEventKind.PasswordChanged,
        EventName = AuthSecurityEvents.FactorChanged.Name,
        Factor = "password",
        Client = SecurityHistoryClient.Firefox,
        DeviceClass = AuthDeviceClass.Mobile,
        Network = "198.51.100.0/24",
        CorrelationId = "0f8fad5b-d9cb-469f-a165-70867728950e",
        OccurredAt = OccurredAt,
    };
}
