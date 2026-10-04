using backend.main.features.media;
using backend.main.shared.storage;

using FluentAssertions;

namespace backend.tests.Unit.Features.Media;

public class MediaValidationRecorderTests
{
    [Fact]
    public async Task RecordAsync_ShouldMarkTheAssetReady_AndDeleteTheQuarantinedBytes()
    {
        await using var harness = await Harness.CreateAsync(attemptCount: 1);

        var recorded = await harness.Recorder.RecordAsync(
            harness.Asset, 1, MediaValidationOutcome.Accept("image/webp", 640, 480, 12345));

        recorded.Should().BeTrue();
        var stored = await harness.Database.ReloadAsync(harness.Asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Ready);
        stored.ContentType.Should().Be("image/webp");
        stored.Width.Should().Be(640);
        stored.Height.Should().Be(480);
        stored.ByteSize.Should().Be(12345);
        stored.ValidatedAt.Should().Be(harness.Database.Time.GetUtcNow().UtcDateTime);
        stored.QuarantineBlobPath.Should().BeNull();
        harness.Blobs.Quarantine.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_ShouldMarkTheAssetRejected_WithTheReason()
    {
        await using var harness = await Harness.CreateAsync(attemptCount: 1);

        await harness.Recorder.RecordAsync(harness.Asset, 1, MediaValidationOutcome.Reject("Too blurry."));

        var stored = await harness.Database.ReloadAsync(harness.Asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Rejected);
        stored.RejectionReason.Should().Be("Too blurry.");
        harness.Blobs.Quarantine.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordAsync_ShouldFallBackToTheGenericReason_WhenNoneWasGiven(string reason)
    {
        await using var harness = await Harness.CreateAsync(attemptCount: 1);

        await harness.Recorder.RecordAsync(harness.Asset, 1, MediaValidationOutcome.Reject(reason));

        (await harness.Database.ReloadAsync(harness.Asset.Id)).RejectionReason
            .Should().Be(MediaAssetService.GenericRejectionMessage);
    }

    [Fact]
    public async Task RecordAsync_ShouldPromoteOnce_WhenTheSameOutcomeIsRecordedTwice()
    {
        // Kafka redelivers the worker's result; the second copy must change nothing.
        await using var harness = await Harness.CreateAsync(attemptCount: 1);
        var outcome = MediaValidationOutcome.Accept("image/webp", 10, 10, 100);
        await harness.Recorder.RecordAsync(harness.Asset, 1, outcome);
        var first = await harness.Database.ReloadAsync(harness.Asset.Id);
        harness.Database.Time.Advance(TimeSpan.FromMinutes(1));

        var again = await harness.Recorder.RecordAsync(harness.Asset, 1, outcome);

        again.Should().BeFalse();
        var second = await harness.Database.ReloadAsync(harness.Asset.Id);
        second.Status.Should().Be(MediaAssetStatus.Ready);
        second.UpdatedAt.Should().Be(first.UpdatedAt);
        second.ValidatedAt.Should().Be(first.ValidatedAt);
        harness.Blobs.DeletedQuarantinePaths.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordAsync_ShouldIgnoreAStaleClaimsOutcome_AndKeepTheBytesForTheCurrentHolder()
    {
        // Claim 1 went quiet and the reconciler re-drove it as claim 2. Claim 1's late verdict
        // must neither settle the asset nor delete the bytes claim 2 is about to read.
        await using var harness = await Harness.CreateAsync(attemptCount: 2);

        var recorded = await harness.Recorder.RecordAsync(harness.Asset, 1, MediaValidationOutcome.Reject("late"));

        recorded.Should().BeFalse();
        var stored = await harness.Database.ReloadAsync(harness.Asset.Id);
        stored.Status.Should().Be(MediaAssetStatus.Processing);
        stored.RejectionReason.Should().BeNull();
        harness.Blobs.Quarantine.Should().ContainKey(harness.Asset.QuarantineBlobPath!);
    }

    [Fact]
    public async Task RecordAsync_ShouldAcceptTheVerdictOfAReleasedClaim_WhileNoNewerClaimExists()
    {
        // The claim was handed back — its publish timed out yet was delivered, or the reconciler
        // released it — and the worker's answer for it arrives afterwards. Dropping it would only
        // mean decoding the same bytes again.
        await using var harness = await Harness.CreateAsync(attemptCount: 2);
        await harness.Database.CreateRepository().TryTransitionAsync(
            harness.Asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Uploaded, whenAttempt: 2);

        var recorded = await harness.Recorder.RecordAsync(
            harness.Asset, 2, MediaValidationOutcome.Accept("image/webp", 10, 10, 100));

        recorded.Should().BeTrue();
        (await harness.Database.ReloadAsync(harness.Asset.Id)).Status.Should().Be(MediaAssetStatus.Ready);
        harness.Blobs.Quarantine.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_ShouldIgnoreAReleasedAssetsVerdict_FromAnOlderClaim()
    {
        await using var harness = await Harness.CreateAsync(attemptCount: 2);
        await harness.Database.CreateRepository().TryTransitionAsync(
            harness.Asset.Id, MediaAssetStatus.Processing, MediaAssetStatus.Uploaded, whenAttempt: 2);

        var recorded = await harness.Recorder.RecordAsync(harness.Asset, 1, MediaValidationOutcome.Reject("late"));

        recorded.Should().BeFalse();
        (await harness.Database.ReloadAsync(harness.Asset.Id)).Status.Should().Be(MediaAssetStatus.Uploaded);
        harness.Blobs.Quarantine.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RecordAsync_ShouldNotOverturnARejection_WithALateAcceptance()
    {
        await using var harness = await Harness.CreateAsync(attemptCount: 1);
        await harness.Recorder.RecordAsync(harness.Asset, 1, MediaValidationOutcome.Reject("refused"));

        var recorded = await harness.Recorder.RecordAsync(
            harness.Asset, 1, MediaValidationOutcome.Accept("image/webp", 1, 1, 1));

        recorded.Should().BeFalse();
        (await harness.Database.ReloadAsync(harness.Asset.Id)).Status.Should().Be(MediaAssetStatus.Rejected);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public MediaTestDatabase Database { get; private init; } = null!;
        public InMemoryBlobStore Blobs { get; private init; } = null!;
        public MediaAsset Asset { get; private init; } = null!;
        public MediaValidationRecorder Recorder { get; private init; } = null!;

        /// <summary>An asset Processing under claim <paramref name="attemptCount"/>, its bytes in quarantine.</summary>
        public static async Task<Harness> CreateAsync(int attemptCount)
        {
            var database = await MediaTestDatabase.CreateAsync();
            var asset = await database.SeedAssetAsync(MediaAssetStatus.Processing, attemptCount: attemptCount);
            var blobs = new InMemoryBlobStore();
            blobs.Put(asset.QuarantineBlobPath!, InMemoryBlobStore.Image());

            return new Harness
            {
                Database = database,
                Blobs = blobs,
                Asset = asset,
                Recorder = new MediaValidationRecorder(database.CreateRepository(), blobs, database.Time)
            };
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
