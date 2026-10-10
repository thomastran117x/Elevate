using System.Reflection;

using backend.main.shared.providers.messages;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;
using backend.tests.Unit.Features.Media;
using backend.tests.Unit.Workers.MediaValidation;
using backend.worker.email_worker;
using backend.worker.media_worker;
using backend.worker.sms_worker;

using Confluent.Kafka;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    [Fact]
    public async Task KafkaMediaWorker_ShouldExitImmediately_WhenStorageIsNotConfigured()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var consumersBuilt = 0;
        var worker = new KafkaMediaWorker(
            scopeFactory.Object,
            MediaOptions(connectionString: null),
            () =>
            {
                consumersBuilt++;
                return Mock.Of<IConsumer<string, string>>();
            });

        await InvokeExecuteAsync(worker, CancellationToken.None);

        consumersBuilt.Should().Be(0);
        scopeFactory.Verify(factory => factory.CreateScope(), Times.Never);
    }

    [Fact]
    public void KafkaMediaWorker_ShouldBuildConsumer_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var worker = new KafkaMediaWorker(Mock.Of<IServiceScopeFactory>(), MediaOptions());

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
    public async Task KafkaMediaWorker_ShouldProcessThenCommit_AndKeepConsumingPastAPoisonMessage()
    {
        using var stopping = new CancellationTokenSource();
        var media = new MediaWorkerLoopHarness();
        media.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        var poison = Result("{not json", offset: 1);
        var valid = Result(MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()).Payload, offset: 2);
        media.Consumer.SetupSequence(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(poison)
            .Returns(valid)
            .Returns(() => throw Stop(stopping));

        await InvokeExecuteAsync(media.Worker(), stopping.Token);

        media.DlqPublisher.Verify(
            p => p.PublishAsync(It.Is<MediaWorkerEnvelope>(e => e.Offset == 1), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        media.StatusPublisher.Verify(
            p => p.PublishAsync(It.Is<MediaValidationResultMessage>(m => m.Accepted), It.IsAny<CancellationToken>()),
            Times.Once);
        media.Consumer.Verify(c => c.Subscribe("media-topic"), Times.Once);
        media.Consumer.Verify(c => c.Commit(poison), Times.Once);
        media.Consumer.Verify(c => c.Commit(valid), Times.Once);
    }

    [Fact]
    public async Task KafkaMediaWorker_ShouldLeaveTheOffsetUncommitted_WhenProcessingFails()
    {
        using var stopping = new CancellationTokenSource();
        var media = new MediaWorkerLoopHarness();
        media.Blobs.Put(MediaWorkerTestMessages.QuarantinePath, InMemoryBlobStore.Image());
        var result = Result(MediaWorkerTestMessages.Envelope(MediaWorkerTestMessages.Request()).Payload, offset: 7);
        media.Consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>())).Returns(result);
        media.StatusPublisher
            .Setup(p => p.PublishAsync(It.IsAny<MediaValidationResultMessage>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                // Stop during the reconnect delay that follows, so the test does not wait it out.
                stopping.Cancel();
                throw new InvalidOperationException("broker unavailable");
            });

        var act = () => InvokeExecuteAsync(media.Worker(), stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        media.Consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    [Fact]
    public async Task KafkaMediaWorker_ShouldWaitToReconnect_AfterAConsumeError()
    {
        using var stopping = new CancellationTokenSource();
        var media = new MediaWorkerLoopHarness();
        media.Consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                stopping.Cancel();
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]>(), new Error(ErrorCode.BrokerNotAvailable));
            });

        var act = () => InvokeExecuteAsync(media.Worker(), stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        media.Consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    private static MediaWorkerOptions MediaOptions(string? connectionString = "UseDevelopmentStorage=true") =>
        new("localhost:9092", "media-topic", "media-group", "media-dlq", "media-status", connectionString, "public", "quarantine");

    private static ConsumeResult<string, string> Result(string payload, long offset) => new()
    {
        Topic = "media-topic",
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, string> { Key = offset.ToString(), Value = payload }
    };

    private static OperationCanceledException Stop(CancellationTokenSource stopping)
    {
        stopping.Cancel();
        return new OperationCanceledException(stopping.Token);
    }

    /// <summary>
    /// A media worker whose consumer is a mock and whose processor runs the real pipeline over
    /// in-memory storage, so a test drives the actual consume loop message by message.
    /// </summary>
    private sealed class MediaWorkerLoopHarness
    {
        public MediaWorkerLoopHarness()
        {
            DlqPublisher
                .Setup(p => p.PublishAsync(It.IsAny<MediaWorkerEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            StatusPublisher
                .Setup(p => p.PublishAsync(It.IsAny<MediaValidationResultMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public InMemoryBlobStore Blobs { get; } = new();
        public Mock<IConsumer<string, string>> Consumer { get; } = new();
        public Mock<IMediaWorkerDlqPublisher> DlqPublisher { get; } = new();
        public Mock<IMediaValidationStatusPublisher> StatusPublisher { get; } = new();

        public KafkaMediaWorker Worker()
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => new MediaWorkerMessageProcessor(
                new MediaValidationPipeline(
                    Blobs,
                    new NetVipsImageProcessor(Options.Create(new ImageProcessingOptions()))),
                DlqPublisher.Object,
                StatusPublisher.Object));
            var provider = services.BuildServiceProvider();

            return new KafkaMediaWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                MediaOptions(),
                () => Consumer.Object);
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

