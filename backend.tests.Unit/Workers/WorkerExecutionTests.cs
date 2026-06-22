using System.Reflection;

using backend.worker.email_worker;
using backend.worker.sms_worker;

using Confluent.Kafka;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Workers;

public class WorkerExecutionTests
{
    [Fact]
    public async Task KafkaEmailWorker_ShouldExitImmediately_WhenNotConfigured()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var worker = new KafkaEmailWorker(
            scopeFactory.Object,
            new EmailWorkerOptions("kafka", "email", "group", "dlq", "status", null, 587, "user", "pass", "http://localhost")
        );

        await InvokeExecuteAsync(worker, CancellationToken.None);

        scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
    }

    [Fact]
    public async Task KafkaSmsWorker_ShouldExitImmediately_WhenNotConfigured()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var worker = new KafkaSmsWorker(
            scopeFactory.Object,
            new SmsWorkerOptions("kafka", "sms", "group", "dlq", null, "token", "mg-service", null)
        );

        await InvokeExecuteAsync(worker, CancellationToken.None);

        scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
    }

    [Fact]
    public void KafkaEmailWorker_ShouldBuildConsumer_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var worker = new KafkaEmailWorker(
                Mock.Of<IServiceScopeFactory>(),
                new EmailWorkerOptions("localhost:9092", "email", "group", "dlq", "status", "smtp", 587, "user", "pass", "http://localhost")
            );

            using var consumer = InvokeBuildConsumer(worker);
            consumer.Should().NotBeNull();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is DllNotFoundException)
        {
            return;
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    [Fact]
    public void KafkaSmsWorker_ShouldBuildConsumer_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var worker = new KafkaSmsWorker(
                Mock.Of<IServiceScopeFactory>(),
                new SmsWorkerOptions("localhost:9092", "sms", "group", "dlq", "sid", "token", "mg-service", null)
            );

            using var consumer = InvokeBuildConsumer(worker);
            consumer.Should().NotBeNull();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is DllNotFoundException)
        {
            return;
        }
        catch (DllNotFoundException)
        {
            return;
        }
    }

    private static Task InvokeExecuteAsync(object worker, CancellationToken cancellationToken)
    {
        var method = worker.GetType().GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(worker, [cancellationToken])!;
    }

    private static IConsumer<string, string> InvokeBuildConsumer(object worker)
    {
        var method = worker.GetType().GetMethod("BuildConsumer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IConsumer<string, string>)method.Invoke(worker, null)!;
    }
}

