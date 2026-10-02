using System.Reflection;
using System.Text.Json;

using backend.main.features.media;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.storage;

using Confluent.Kafka;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Features.Media;

public class MediaValidationStatusConsumerTests
{
    [Fact]
    public async Task Consumer_ShouldRecordEachResult_ThenCommit_AndSkipWhatItCannotRead()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.Database.SeedAssetAsync(MediaAssetStatus.Processing, attemptCount: 1);
        harness.Blobs.Put(asset.QuarantineBlobPath!, InMemoryBlobStore.Image());
        using var stopping = new CancellationTokenSource();
        var unreadable = Result("{not json", 1);
        var noAttempt = Result(Serialize(new MediaValidationResultMessage { MediaAssetId = asset.PublicId }), 2);
        var accepted = Result(Serialize(Accepted(asset.PublicId, attempt: 1)), 3);
        harness.Consumer.SetupSequence(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(unreadable)
            .Returns(noAttempt)
            .Returns(accepted)
            .Returns(() =>
            {
                stopping.Cancel();
                throw new OperationCanceledException(stopping.Token);
            });

        await harness.RunAsync(stopping.Token);

        var stored = await harness.Database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Ready);
        stored.Width.Should().Be(640);
        harness.Blobs.Quarantine.Should().BeEmpty();
        harness.Consumer.Verify(c => c.Subscribe("media-status"), Times.Once);
        harness.Consumer.Verify(c => c.Commit(unreadable), Times.Once);
        harness.Consumer.Verify(c => c.Commit(noAttempt), Times.Once);
        harness.Consumer.Verify(c => c.Commit(accepted), Times.Once);
    }

    [Fact]
    public async Task Consumer_ShouldPromoteOnce_WhenTheSameResultIsDeliveredTwice()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.Database.SeedAssetAsync(MediaAssetStatus.Processing, attemptCount: 1);
        harness.Blobs.Put(asset.QuarantineBlobPath!, InMemoryBlobStore.Image());
        using var stopping = new CancellationTokenSource();
        var payload = Serialize(Accepted(asset.PublicId, attempt: 1));
        harness.Consumer.SetupSequence(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(Result(payload, 1))
            .Returns(Result(payload, 2))
            .Returns(() =>
            {
                stopping.Cancel();
                throw new OperationCanceledException(stopping.Token);
            });

        await harness.RunAsync(stopping.Token);

        (await harness.Database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Ready);
        harness.Blobs.DeletedQuarantinePaths.Should().ContainSingle("the replay found the asset already settled");
        harness.Consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Consumer_ShouldLeaveTheOffsetUncommitted_WhenRecordingFails()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.Database.SeedAssetAsync(MediaAssetStatus.Processing, attemptCount: 1);
        using var stopping = new CancellationTokenSource();
        harness.Consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(Result(Serialize(Accepted(asset.PublicId, attempt: 1)), 1));
        harness.FailRecording = () =>
        {
            // Stop during the reconnect delay that follows, so the test does not wait it out.
            stopping.Cancel();
            throw new InvalidOperationException("database unavailable");
        };

        var act = () => harness.RunAsync(stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        harness.Consumer.Verify(c => c.Commit(It.IsAny<ConsumeResult<string, string>>()), Times.Never);
    }

    [Fact]
    public async Task Consumer_ShouldWaitToReconnect_AfterAConsumeError()
    {
        await using var harness = await Harness.CreateAsync();
        using var stopping = new CancellationTokenSource();
        harness.Consumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                stopping.Cancel();
                throw new ConsumeException(new ConsumeResult<byte[], byte[]>(), new Error(ErrorCode.BrokerNotAvailable));
            });

        var act = () => harness.RunAsync(stopping.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecordAsync_ShouldDropAResultForAnAssetThatNoLongerExists()
    {
        await using var harness = await Harness.CreateAsync();
        await using var provider = harness.Services();
        using var scope = provider.CreateScope();

        var act = () => MediaValidationStatusConsumer.RecordAsync(
            scope.ServiceProvider, Accepted(Guid.CreateVersion7(), attempt: 1), CancellationToken.None);

        await act.Should().NotThrowAsync();
        harness.Blobs.DeletedQuarantinePaths.Should().BeEmpty();
    }

    [Fact]
    public void Consumer_ShouldBuildAConsumer_WhenKafkaNativeLibraryIsAvailable()
    {
        try
        {
            var consumer = new MediaValidationStatusConsumer(Mock.Of<IServiceScopeFactory>(), Options());
            var build = typeof(MediaValidationStatusConsumer).GetMethod("BuildConsumer", BindingFlags.Instance | BindingFlags.NonPublic)!;

            using var built = (IConsumer<string, string>)build.Invoke(consumer, null)!;
            built.Should().NotBeNull();
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

    private static MediaValidationStatusConsumerOptions Options() => new("localhost:9092", "media-status", "media-status-group");

    private static MediaValidationResultMessage Accepted(Guid assetId, int attempt) => new()
    {
        MediaAssetId = assetId,
        Attempt = attempt,
        Accepted = true,
        ContentType = "image/webp",
        Width = 640,
        Height = 480,
        ByteSize = 2048
    };

    private static string Serialize(MediaValidationResultMessage message) =>
        JsonSerializer.Serialize(message, JsonOptions.Default);

    private static ConsumeResult<string, string> Result(string payload, long offset) => new()
    {
        Topic = "media-status",
        Partition = new Partition(0),
        Offset = new Offset(offset),
        Message = new Message<string, string> { Value = payload }
    };

    private sealed class Harness : IAsyncDisposable
    {
        public MediaTestDatabase Database { get; private init; } = null!;
        public InMemoryBlobStore Blobs { get; } = new();
        public Mock<IConsumer<string, string>> Consumer { get; } = new();

        /// <summary>When set, replaces recording with a failure, to simulate a database fault.</summary>
        public Action? FailRecording { get; set; }

        public static async Task<Harness> CreateAsync() =>
            new() { Database = await MediaTestDatabase.CreateAsync() };

        public ServiceProvider Services()
        {
            var services = new ServiceCollection();
            services.AddScoped<IMediaAssetRepository>(_ =>
            {
                FailRecording?.Invoke();
                return Database.CreateRepository();
            });
            services.AddScoped<IAzureBlobService>(_ => Blobs);
            services.AddSingleton<TimeProvider>(Database.Time);
            services.AddScoped<MediaValidationRecorder>();
            return services.BuildServiceProvider();
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            await using var provider = Services();
            var consumer = new MediaValidationStatusConsumer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options(),
                () => Consumer.Object);

            var execute = typeof(MediaValidationStatusConsumer)
                .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)execute.Invoke(consumer, [stoppingToken])!;
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
