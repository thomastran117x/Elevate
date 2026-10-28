using System.Reflection;
using System.Text.Json;

using backend.main.infrastructure.database.core;
using backend.main.shared.providers;
using backend.main.shared.providers.messaging;
using backend.tests.Unit.Features.Auth.History;
using backend.worker.security_history_worker;

using Confluent.Kafka;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using static backend.tests.Unit.Workers.SecurityHistory.SecurityHistoryWorkerTestSupport;

namespace backend.tests.Unit.Workers.SecurityHistory;

public class KafkaSecurityHistoryWorkerTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldStoreThenCommit_AndKeepConsumingPastAPoisonMessage()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        var consumer = new Mock<IConsumer<string, string>>();
        var poison = Result("{ not json", 1);
        var valid = Result(JsonSerializer.Serialize(Message(userId: SecurityHistoryDbHarness.OwnerId), JsonOptions.Default), 2);
        var results = new Queue<ConsumeResult<string, string>>([poison, valid]);
        consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(() => results.Count > 0 ? results.Dequeue() : throw Stop(stopping));

        await InvokeExecuteAsync(Worker(harness, consumer.Object, dlq.Object), stopping.Token);

        consumer.Verify(c => c.Subscribe("eventxperience-security-history"), Times.Once);
        consumer.Verify(c => c.Commit(poison), Times.Once);
        consumer.Verify(c => c.Commit(valid), Times.Once);
        dlq.Verify(d => d.PublishAsync(It.IsAny<KafkaMessageEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        (await harness.Db.AccountSecurityEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLeaveTheOffset_WhenTheDlqIsUnreachable()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var dlq = new Mock<ISecurityHistoryWorkerDlqPublisher>();
        dlq.Setup(d => d.PublishAsync(It.IsAny<KafkaMessageEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => stopping.Cancel())
            .ThrowsAsync(new InvalidOperationException("DLQ unreachable"));
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>())).Returns(Result("{ not json", 1));

        var act = () => InvokeExecuteAsync(Worker(harness, consumer.Object, dlq.Object), stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWaitToReconnect_AfterAConsumeError()
    {
        await using var harness = await SecurityHistoryDbHarness.CreateAsync();
        using var stopping = new CancellationTokenSource();
        var consumer = new Mock<IConsumer<string, string>>();
        consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                stopping.Cancel();
                throw new ConsumeException(new ConsumeResult<byte[], byte[]>(), new Error(ErrorCode.BrokerNotAvailable));
            });

        var act = () => InvokeExecuteAsync(Worker(harness, consumer.Object, Mock.Of<ISecurityHistoryWorkerDlqPublisher>()), stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotConsume_UntilTheHistoryTableExists()
    {
        using var stopping = new CancellationTokenSource();
        var probes = 0;
        var services = new ServiceCollection();
        // An empty database: the API has not migrated yet.
        services.AddDbContext<AppDatabaseContext>(options => options.UseSqlite("Data Source=:memory:"));
        var provider = services.BuildServiceProvider();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(() =>
        {
            if (++probes == 2)
                stopping.Cancel();
            return provider.CreateScope();
        });
        var consumersBuilt = 0;
        var worker = new KafkaSecurityHistoryWorker(
            scopeFactory.Object,
            Options(),
            () =>
            {
                consumersBuilt++;
                return Mock.Of<IConsumer<string, string>>();
            },
            TimeSpan.Zero);

        await InvokeExecuteAsync(worker, stopping.Token);

        probes.Should().Be(2);
        consumersBuilt.Should().Be(0);
    }

    [Fact]
    public void BuildConsumer_ShouldBuild_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var worker = new KafkaSecurityHistoryWorker(Mock.Of<IServiceScopeFactory>(), Options());
            var method = typeof(KafkaSecurityHistoryWorker).GetMethod("BuildConsumer", BindingFlags.Instance | BindingFlags.NonPublic)!;

            using var consumer = (IConsumer<string, string>)method.Invoke(worker, null)!;

            consumer.Should().NotBeNull();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is DllNotFoundException)
        {
        }
        catch (DllNotFoundException)
        {
        }
    }

    private static KafkaSecurityHistoryWorker Worker(
        SecurityHistoryDbHarness harness,
        IConsumer<string, string> consumer,
        ISecurityHistoryWorkerDlqPublisher dlq)
    {
        var options = Options();
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.CreateContext());
        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<AppDatabaseContext>();
            var metrics = Metrics().Metrics;
            return new SecurityHistoryMessageProcessor(
                new SecurityHistoryWriter(db, options, new HistoryClock(Now)),
                dlq,
                metrics);
        });
        var provider = services.BuildServiceProvider();

        return new KafkaSecurityHistoryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            () => consumer,
            TimeSpan.Zero);
    }

    private static ConsumeResult<string, string> Result(string payload, long offset) => new()
    {
        Topic = "eventxperience-security-history",
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, string> { Key = offset.ToString(), Value = payload }
    };

    private static OperationCanceledException Stop(CancellationTokenSource stopping)
    {
        stopping.Cancel();
        return new OperationCanceledException(stopping.Token);
    }

    private static Task InvokeExecuteAsync(KafkaSecurityHistoryWorker worker, CancellationToken cancellationToken)
    {
        var method = typeof(KafkaSecurityHistoryWorker).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(worker, [cancellationToken])!;
    }
}
