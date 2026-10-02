using backend.main.features.media;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Features.Media;

public class MediaValidationReconcilerRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_ShouldReclaimAndRedrive_AClaimTheWorkerNeverAnswered()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.SeedAsync(MediaAssetStatus.Processing, attemptCount: 1, quietFor: TimeSpan.FromMinutes(3));

        var result = await harness.Runner.RunOnceAsync();

        result.Should().Be(new MediaValidationReconcileResult(Redriven: 1, Parked: 0));
        var stored = await harness.Database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Processing);
        stored.AttemptCount.Should().Be(2, "the re-drive is a new claim, so the old one's late verdict is ignored");
        harness.Dispatched.Should().ContainSingle()
            .Which.Should().Be((asset.PublicId, 2, MediaValidationReconcilerRunner.RedriveSubject));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldRedrive_AnAttachedAssetNobodyClaimed()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.SeedAsync(MediaAssetStatus.Uploaded, attemptCount: 0, quietFor: TimeSpan.FromMinutes(3));

        await harness.Runner.RunOnceAsync();

        (await harness.Database.ReloadAsync(asset.Id)).AttemptCount.Should().Be(1);
        harness.Dispatched.Should().ContainSingle().Which.Attempt.Should().Be(1);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldBackOff_ByHowOftenTheAssetHasBeenClaimed()
    {
        // Third claim: due only after eight minutes of silence.
        await using var harness = await Harness.CreateAsync();
        var notYet = await harness.SeedAsync(MediaAssetStatus.Processing, attemptCount: 3, quietFor: TimeSpan.FromMinutes(5));
        var due = await harness.SeedAsync(MediaAssetStatus.Processing, attemptCount: 3, quietFor: TimeSpan.FromMinutes(9));

        var result = await harness.Runner.RunOnceAsync();

        result.Redriven.Should().Be(1);
        harness.Dispatched.Should().ContainSingle().Which.AssetId.Should().Be(due.PublicId);
        (await harness.Database.ReloadAsync(notYet.Id)).AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLeaveARecentClaimAlone()
    {
        await using var harness = await Harness.CreateAsync();
        await harness.SeedAsync(MediaAssetStatus.Processing, attemptCount: 1, quietFor: TimeSpan.FromSeconds(30));

        var result = await harness.Runner.RunOnceAsync();

        result.Should().Be(new MediaValidationReconcileResult(0, 0));
        harness.Dispatched.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MediaAssetStatus.Processing)]
    [InlineData(MediaAssetStatus.Uploaded)]
    public async Task RunOnceAsync_ShouldParkAnAssetForReview_AfterTheLastAttempt(MediaAssetStatus status)
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.SeedAsync(
            status, attemptCount: MediaValidationReconcilerRunner.MaxAttempts, quietFor: TimeSpan.FromHours(1));

        var result = await harness.Runner.RunOnceAsync();

        result.Should().Be(new MediaValidationReconcileResult(Redriven: 0, Parked: 1));
        var stored = await harness.Database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.NeedsReview);
        stored.QuarantineBlobPath.Should().NotBeNull("a reviewer needs the bytes");
        harness.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldNotTouchAnAssetWhoseVerdictArrivedFirst()
    {
        // Read as stalled, but the worker's result was recorded before the reconciler moved it.
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.SeedAsync(MediaAssetStatus.Processing, attemptCount: 1, quietFor: TimeSpan.FromMinutes(3));
        var staleRead = await harness.Database.ReloadAsync(asset.Id);
        var real = harness.Database.CreateRepository();
        await real.TryTransitionAsync(asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Ready, whenAttempt: 1);
        var repository = new Mock<IMediaAssetRepository>();
        repository.Setup(r => r.GetStalledBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([staleRead]);
        repository.Setup(r => r.TryTransitionAsync(
                It.IsAny<int>(),
                It.IsAny<MediaAssetStatus>(),
                It.IsAny<MediaAssetStatus>(),
                It.IsAny<MediaAssetChanges?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .Returns((int id, MediaAssetStatus from, MediaAssetStatus to, MediaAssetChanges? changes, CancellationToken ct, int? attempt) =>
                real.TryTransitionAsync(id, from, to, changes, ct, attempt));
        var runner = new MediaValidationReconcilerRunner(repository.Object, harness.Dispatcher.Object, harness.Database.Time);

        var result = await runner.RunOnceAsync();

        result.Should().Be(new MediaValidationReconcileResult(0, 0));
        (await harness.Database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Ready);
        harness.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldHandTheClaimBack_WhenTheRequestCannotBePublished()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.SeedAsync(MediaAssetStatus.Uploaded, attemptCount: 1, quietFor: TimeSpan.FromMinutes(3));
        harness.Dispatcher.Setup(d => d.DispatchAsync(It.IsAny<MediaAsset>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotAvailableException("broker down"));

        var result = await harness.Runner.RunOnceAsync();

        result.Redriven.Should().Be(0);
        var stored = await harness.Database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Uploaded);
        stored.AttemptCount.Should().Be(2, "the failed claim still counts toward giving up");
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(4, 16)]
    [InlineData(5, 30)]
    [InlineData(50, 30)]
    public void BackoffFor_ShouldDoublePerClaim_UpToTheCap(int attemptCount, int minutes)
    {
        MediaValidationReconcilerRunner.BackoffFor(attemptCount).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public async Task MediaValidationReconciler_ShouldRunTheRunnerOnStart_AndStopCleanly()
    {
        await using var harness = await Harness.CreateAsync();
        var asset = await harness.SeedAsync(MediaAssetStatus.Uploaded, attemptCount: 0, quietFor: TimeSpan.FromMinutes(3));
        var services = new ServiceCollection();
        services.AddScoped(_ => harness.Runner);
        await using var provider = services.BuildServiceProvider();
        var reconciler = new MediaValidationReconciler(provider);

        await reconciler.StartAsync(CancellationToken.None);
        for (var i = 0; i < 100 && harness.Dispatched.Count == 0; i++)
            await Task.Delay(20);
        await reconciler.StopAsync(CancellationToken.None);

        harness.Dispatched.Should().ContainSingle().Which.AssetId.Should().Be(asset.PublicId);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public MediaTestDatabase Database { get; private init; } = null!;
        public Mock<IMediaValidationDispatcher> Dispatcher { get; } = new();
        public List<(Guid AssetId, int Attempt, string Subject)> Dispatched { get; } = [];
        public MediaValidationReconcilerRunner Runner { get; private set; } = null!;

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness { Database = await MediaTestDatabase.CreateAsync() };
            harness.Dispatcher
                .Setup(d => d.DispatchAsync(It.IsAny<MediaAsset>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<MediaAsset, int, string, CancellationToken>((asset, attempt, subject, _) =>
                    harness.Dispatched.Add((asset.PublicId, attempt, subject)))
                .Returns(Task.CompletedTask);
            harness.Runner = new MediaValidationReconcilerRunner(
                harness.Database.CreateRepository(), harness.Dispatcher.Object, harness.Database.Time);
            return harness;
        }

        public Task<MediaAsset> SeedAsync(MediaAssetStatus status, int attemptCount, TimeSpan quietFor)
        {
            var now = Database.Time.GetUtcNow().UtcDateTime;
            return Database.SeedAssetAsync(
                status, createdAt: now.AddHours(-1), updatedAt: now - quietFor, attemptCount: attemptCount);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
