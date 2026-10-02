using Azure;

using backend.main.features.media;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using backend.main.shared.storage;

namespace backend.tests.Unit.Features.Media;

public class QuarantineReaperRunnerTests
{
    [Fact]
    public async Task RunOnceAsync_ShouldExpireUploadsNeverAttachedWithinADay_AndDeleteTheirBytes()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var now = database.Time.GetUtcNow();
        var stale = await database.SeedAssetAsync(
            MediaAssetStatus.PendingUpload, quarantineBlobPath: "events/stale.png", createdAt: now.UtcDateTime.AddHours(-25));
        var fresh = await database.SeedAssetAsync(
            MediaAssetStatus.PendingUpload, quarantineBlobPath: "events/fresh.png", createdAt: now.UtcDateTime.AddHours(-1));
        blobs.Put("events/stale.png", InMemoryBlobStore.Image(), lastModified: now.AddHours(-25));
        blobs.Put("events/fresh.png", InMemoryBlobStore.Image(), lastModified: now.AddHours(-1));

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.ExpiredAssets.Should().Be(1);
        var expired = await database.ReloadAsync(stale.Id);
        expired.Status.Should().Be(MediaAssetStatus.Rejected);
        expired.RejectionReason.Should().Be(QuarantineReaperRunner.ExpiredMessage);
        expired.QuarantineBlobPath.Should().BeNull();
        blobs.Quarantine.Should().NotContainKey("events/stale.png");

        (await database.ReloadAsync(fresh.Id)).Status.Should().Be(MediaAssetStatus.PendingUpload);
        blobs.Quarantine.Should().ContainKey("events/fresh.png");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldExpireAnOldPendingAsset_EvenWhenItNeverGotABlob()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var asset = await database.SeedAssetAsync(
            MediaAssetStatus.PendingUpload, createdAt: database.Time.GetUtcNow().UtcDateTime.AddDays(-3));

        await CreateRunner(database, new InMemoryBlobStore()).RunOnceAsync();

        (await database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Rejected);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldDeleteOldQuarantineBlobsThatNoAssetIsWaitingOn()
    {
        // The uploader deleted their account, taking the asset row with it; or a delete failed
        // after the asset was rejected. Either way nothing will ever claim these bytes.
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var old = database.Time.GetUtcNow().AddHours(-30);
        blobs.Put("events/orphan.png", InMemoryBlobStore.Image(), lastModified: old);

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.DeletedBlobs.Should().Be(1);
        blobs.Quarantine.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldNeverDeleteBytesAnAttachIsProcessing()
    {
        // Issued a day ago, but claimed a moment ago: a live request is decoding these bytes.
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var now = database.Time.GetUtcNow();
        var asset = await database.SeedAssetAsync(
            MediaAssetStatus.Processing,
            quarantineBlobPath: "events/busy.png",
            createdAt: now.UtcDateTime.AddHours(-30),
            updatedAt: now.UtcDateTime.AddSeconds(-20));
        blobs.Put("events/busy.png", InMemoryBlobStore.Image(), lastModified: now.AddHours(-30));

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.Should().Be(new QuarantineReapResult(ExpiredAssets: 0, DeletedBlobs: 0, ReleasedClaims: 0));
        (await database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Processing);
        blobs.Quarantine.Should().ContainKey("events/busy.png");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldReleaseAClaimWhoseAttachDied_AndKeepItsBytesForARetry()
    {
        // The process was killed mid-pipeline, so nothing released the claim. Recently issued, so
        // the user can still retry, and the retry needs the quarantined bytes.
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var now = database.Time.GetUtcNow();
        var asset = await database.SeedAssetAsync(
            MediaAssetStatus.Processing,
            quarantineBlobPath: "events/abandoned.png",
            createdAt: now.UtcDateTime.AddMinutes(-15),
            updatedAt: now.UtcDateTime - MediaAssetService.StaleProcessingAfter - TimeSpan.FromSeconds(1));
        blobs.Put("events/abandoned.png", InMemoryBlobStore.Image(), lastModified: now.AddMinutes(-15));

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.ReleasedClaims.Should().Be(1);
        result.ExpiredAssets.Should().Be(0);
        var stored = await database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Uploaded);
        stored.QuarantineBlobPath.Should().Be("events/abandoned.png");
        blobs.Quarantine.Should().ContainKey("events/abandoned.png");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldReapAClaimThatDiedADayAgo_RowAndBytes()
    {
        // Nobody retried: the claim is released and, being a day old, expired in the same run.
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var dayAgo = database.Time.GetUtcNow().AddHours(-30);
        var asset = await database.SeedAssetAsync(
            MediaAssetStatus.Processing, quarantineBlobPath: "events/stuck.png", createdAt: dayAgo.UtcDateTime);
        blobs.Put("events/stuck.png", InMemoryBlobStore.Image(), lastModified: dayAgo);

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.Should().Be(new QuarantineReapResult(ExpiredAssets: 1, DeletedBlobs: 0, ReleasedClaims: 1));
        var stored = await database.ReloadAsync(asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Rejected);
        stored.RejectionReason.Should().Be(QuarantineReaperRunner.ExpiredMessage);
        blobs.Quarantine.Should().BeEmpty();
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLeaveYoungBlobsAlone()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        blobs.Put("events/young.png", InMemoryBlobStore.Image(), lastModified: database.Time.GetUtcNow().AddHours(-2));

        await CreateRunner(database, blobs).RunOnceAsync();

        blobs.Quarantine.Should().ContainKey("events/young.png");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLeaveABlobToTheExpiryPass_WhileItsAssetIsStillWaiting()
    {
        // The expiry pass took its batch and stopped before this asset. Its bytes are deleted when
        // the asset is expired, not by the blob sweep under it.
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var now = database.Time.GetUtcNow();
        for (var i = 0; i < QuarantineReaperRunner.BatchSize; i++)
            await database.SeedAssetAsync(createdAt: now.UtcDateTime.AddHours(-48));
        var waiting = await database.SeedAssetAsync(
            MediaAssetStatus.Uploaded, quarantineBlobPath: "events/waiting.png", createdAt: now.UtcDateTime.AddHours(-26));
        blobs.Put("events/waiting.png", InMemoryBlobStore.Image(), lastModified: now.AddHours(-26));

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.ExpiredAssets.Should().Be(QuarantineReaperRunner.BatchSize);
        (await database.ReloadAsync(waiting.Id)).Status.Should().Be(MediaAssetStatus.Uploaded);
        blobs.Quarantine.Should().ContainKey("events/waiting.png");
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStopDeletingAtTheBatchSize()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var old = database.Time.GetUtcNow().AddHours(-30);
        for (var i = 0; i < QuarantineReaperRunner.BatchSize + 5; i++)
            blobs.Put($"events/orphan-{i}.png", [0x01], lastModified: old);

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.DeletedBlobs.Should().Be(QuarantineReaperRunner.BatchSize);
        blobs.Quarantine.Should().HaveCount(5);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStillExpireAssets_WhenTheQuarantineContainerIsMissing()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore
        {
            FailListing = new RequestFailedException(404, "The specified container does not exist.", "ContainerNotFound", null)
        };
        await database.SeedAssetAsync(createdAt: database.Time.GetUtcNow().UtcDateTime.AddDays(-2));

        var result = await CreateRunner(database, blobs).RunOnceAsync();

        result.Should().Be(new QuarantineReapResult(ExpiredAssets: 1, DeletedBlobs: 0));
    }

    [Fact]
    public async Task RunOnceAsync_ShouldLetOtherStorageFaultsSurface()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore
        {
            FailListing = new RequestFailedException(503, "Server busy.", "ServerBusy", null)
        };

        var act = () => CreateRunner(database, blobs).RunOnceAsync();

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public async Task QuarantineReaper_ShouldRunTheRunnerOnStart_AndStopCleanly()
    {
        await using var database = await MediaTestDatabase.CreateAsync();
        var blobs = new InMemoryBlobStore();
        var asset = await database.SeedAssetAsync(createdAt: database.Time.GetUtcNow().UtcDateTime.AddDays(-2));

        var services = new ServiceCollection();
        services.AddSingleton(database.Db);
        services.AddSingleton<TimeProvider>(database.Time);
        services.AddSingleton<IAzureBlobService>(blobs);
        services.AddScoped<IMediaAssetRepository>(_ => database.CreateRepository());
        services.AddScoped<QuarantineReaperRunner>();
        await using var provider = services.BuildServiceProvider();

        var reaper = new QuarantineReaper(provider);
        await reaper.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await database.ReloadAsync(asset.Id)).Status != MediaAssetStatus.Rejected && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        await reaper.StopAsync(CancellationToken.None);

        (await database.ReloadAsync(asset.Id)).Status.Should().Be(MediaAssetStatus.Rejected);
    }

    private static QuarantineReaperRunner CreateRunner(MediaTestDatabase database, InMemoryBlobStore blobs) =>
        new(database.CreateRepository(), blobs, database.Time);
}
