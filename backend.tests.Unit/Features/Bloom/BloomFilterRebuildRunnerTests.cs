using backend.main.features.bloom;
using backend.main.features.cache;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Bloom;

public class BloomFilterRebuildRunnerTests
{
    private const string Target = BloomFilterTargets.Username;

    [Fact]
    public async Task RunOnceAsync_ShouldPublishANewGeneration_BuiltFromTheSource()
    {
        var cache = CreatePublishableCache(currentGeneration: 4);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada", "grace"));

        await runner.RunOnceAsync(CancellationToken.None);

        cache.Verify(
            c => c.SetBitmapAsync("bloom:username:bits:5", It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()),
            Times.Once);
        cache.Verify(c => c.SetValueAsync("bloom:username:generation", "5", It.IsAny<TimeSpan?>()), Times.Once);
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
        registry.MightContain(Target, "grace").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldShedValuesThatNoLongerExist()
    {
        // The point of a rebuild: a deleted user or a lapsed reservation frees a name, and only
        // replacing the whole bitmap can clear the bits it left behind. The clock is advanced past
        // the replay window first, since recent local writes are deliberately carried over.
        var cache = CreatePublishableCache(currentGeneration: 1);
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
        var registry = CreateRegistry(cache, clock);
        await registry.RefreshAsync();
        await registry.AddAsync(Target, "departed");

        clock.Advance(TimeSpan.FromHours(2));

        var runner = CreateRunner(registry, cache, new StubSource("ada"));
        await runner.RunOnceAsync(CancellationToken.None);

        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
        registry.MightContain(Target, "departed").Should().Be(BloomFilterLookup.DefinitelyAbsent);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldKeepRecentLocalWrites_EvenIfTheSourceMissedThem()
    {
        // Inside the replay window the value is retained: the rebuild may have read the table
        // before this write committed, and dropping it would be a false negative.
        var cache = CreatePublishableCache(currentGeneration: 1);
        var registry = CreateRegistry(cache);
        await registry.RefreshAsync();
        await registry.AddAsync(Target, "just-signed-up");

        var runner = CreateRunner(registry, cache, new StubSource("ada"));
        await runner.RunOnceAsync(CancellationToken.None);

        registry.MightContain(Target, "just-signed-up").Should().Be(BloomFilterLookup.PossiblyPresent);
    }


    /// <summary>
    /// A signup that commits after the snapshot but before the pointer flips would otherwise be
    /// missing from the new generation, and the filter would call a taken name free.
    /// </summary>
    [Fact]
    public async Task RunOnceAsync_ShouldReplayValuesWrittenDuringTheRebuild()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        cache.Setup(c => c.SetMembersAsync("bloom:username:pending"))
            .ReturnsAsync(["written-mid-rebuild"]);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        registry.MightContain(Target, "written-mid-rebuild").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    /// <summary>
    /// A value committed after the pending snapshot but before the pointer moves lands in the old
    /// generation only. Without a second replay around the flip it would be missing from the newly
    /// published bitmap, and same-generation refreshes never replay pending, so the name would read
    /// as definitely absent until the next rebuild.
    /// </summary>
    [Fact]
    public async Task RunOnceAsync_ShouldReplayValuesThatArriveDuringThePointerFlip()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        cache.SetupSequence(c => c.SetMembersAsync("bloom:username:pending"))
            .ReturnsAsync([])
            .ReturnsAsync(["arrived-during-flip"]);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        registry.MightContain(Target, "arrived-during-flip").Should().Be(BloomFilterLookup.PossiblyPresent);
        cache.Verify(
            c => c.SetBitsAsync("bloom:username:bits:2", It.IsAny<IReadOnlyCollection<long>>()),
            Times.Once);
        cache.Verify(c => c.SetRemoveAsync("bloom:username:pending", "arrived-during-flip"), Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldClearOnlyTheValuesItReplayed()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        cache.Setup(c => c.SetMembersAsync("bloom:username:pending")).ReturnsAsync(["replayed"]);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        cache.Verify(c => c.SetRemoveAsync("bloom:username:pending", "replayed"), Times.Once);
        cache.Verify(c => c.DeleteKeyAsync("bloom:username:pending"), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldReleaseTheRebuildLock()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        cache.Verify(
            c => c.ReleaseLockAsync("bloom:username:rebuild-lock", It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldStillHydrateLocally_WhenTheLockIsHeldElsewhere()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        cache.Setup(c => c.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(false);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        registry.IsReady(Target).Should().BeTrue();
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
        cache.Verify(
            c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()),
            Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldFallBackToALocalFilter_WhenPublishingFails()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        registry.IsReady(Target).Should().BeTrue();
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldSkipBlankValues()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada", "", "grace"));

        var act = () => runner.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        registry.MightContain(Target, "grace").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldIgnoreASourceWithNoConfiguredFilter()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("acme") { Target = BloomFilterTargets.ClubName });

        await runner.RunOnceAsync(CancellationToken.None);

        cache.Verify(
            c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()),
            Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldContinuePastAFailingSource()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(
            registry,
            cache,
            new ThrowingSource(),
            new StubSource("ada"));

        await runner.RunOnceAsync(CancellationToken.None);

        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RunOnceAsync_ShouldPropagateCancellation()
    {
        var cache = CreatePublishableCache(currentGeneration: 1);
        var registry = CreateRegistry(cache);
        var runner = CreateRunner(registry, cache, new StubSource("ada"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => runner.RunOnceAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static BloomFilterRebuildRunner CreateRunner(
        BloomFilterRegistry registry,
        Mock<ICacheService> cache,
        params IBloomFilterSource[] sources) =>
        new(registry, sources, cache.Object, BuildOptions());

    private static BloomFilterRegistry CreateRegistry(Mock<ICacheService> cache, TimeProvider? clock = null) =>
        new(cache.Object, BuildOptions(), clock ?? TimeProvider.System);

    private static IOptions<BloomFilterOptions> BuildOptions() =>
        Options.Create(new BloomFilterOptions
        {
            Targets = new Dictionary<string, BloomFilterTargetOptions>(StringComparer.Ordinal)
            {
                [Target] = new() { ExpectedItems = 10_000, FalsePositiveRate = 0.01 },
            },
        });

    private static Mock<ICacheService> CreatePublishableCache(long currentGeneration)
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("bloom:username:generation"))
            .ReturnsAsync(currentGeneration.ToString());
        cache.Setup(c => c.GetBitmapAsync(It.IsAny<string>()))
            .ReturnsAsync(new byte[BloomFilterMathBytes]);
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        cache.Setup(c => c.SetBitsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetMembersAsync(It.IsAny<string>())).ReturnsAsync([]);
        cache.Setup(c => c.AcquireLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.ReleaseLockAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        return cache;
    }

    private static int BloomFilterMathBytes =>
        new BloomFilterDescriptor(Target, 10_000, 0.01).ByteCount;

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubSource(params string[] values) : IBloomFilterSource
    {
        public string Target { get; init; } = BloomFilterTargets.Username;

        public async IAsyncEnumerable<string> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var value in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return value;
                await Task.Yield();
            }
        }
    }

    private sealed class ThrowingSource : IBloomFilterSource
    {
        public string Target => BloomFilterTargets.Username;

        public async IAsyncEnumerable<string> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("source unavailable");
#pragma warning disable CS0162 // Unreachable: required to make this an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }
}
