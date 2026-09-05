using backend.main.features.bloom;
using backend.main.features.cache;
using backend.main.shared.probabilistic;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Features.Bloom;

public class BloomFilterRegistryTests
{
    private const string Target = BloomFilterTargets.Username;

    [Fact]
    public void MightContain_ShouldReportUnavailable_BeforeHydration()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());

        // Nothing is loaded yet, so the registry must not claim the value is absent.
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.Unavailable);
        registry.IsReady(Target).Should().BeFalse();
    }

    [Fact]
    public void MightContain_ShouldReportUnavailable_ForAnUnknownTarget()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());

        registry.MightContain("club-name", "acme").Should().Be(BloomFilterLookup.Unavailable);
    }

    [Fact]
    public void MightContain_ShouldReportUnavailable_ForAnEmptyValue()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());

        registry.MightContain(Target, string.Empty).Should().Be(BloomFilterLookup.Unavailable);
    }

    [Fact]
    public async Task AddAsync_ShouldMakeTheValueVisibleLocally_EvenWhenRedisIsDown()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetBitsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(false);
        var registry = CreateRegistry(cache);
        registry.InstallLocal(Target, new BloomBitmap(BuildDescriptor().BitCount));

        await registry.AddAsync(Target, "ada");

        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task AddAsync_ShouldIgnoreAnEmptyValueAndUnknownTarget()
    {
        var cache = new Mock<ICacheService>();
        var registry = CreateRegistry(cache);

        await registry.AddAsync(Target, string.Empty);
        await registry.AddAsync("email", "someone@example.com");

        cache.Verify(
            c => c.SetAddAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Adding a value must not make an unhydrated filter start answering lookups. If it did, a
    /// filter holding one username would report DefinitelyAbsent for every other one — the exact
    /// false negative the design forbids.
    /// </summary>
    [Fact]
    public async Task AddAsync_ShouldNotMarkAnUnhydratedFilterReady()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());

        await registry.AddAsync(Target, "ada");

        registry.IsReady(Target).Should().BeFalse();
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.Unavailable);
        registry.MightContain(Target, "someone-else").Should().Be(BloomFilterLookup.Unavailable);
    }

    [Fact]
    public async Task AddAsync_ShouldRecordThePendingValue_SoARebuildCanReplayIt()
    {
        var cache = new Mock<ICacheService>();
        var registry = CreateRegistry(cache);

        await registry.AddAsync(Target, "ada");

        cache.Verify(c => c.SetAddAsync("bloom:username:pending", "ada"), Times.Once);
    }

    [Fact]
    public async Task AddAsync_ShouldNotWriteBits_BeforeAGenerationExists()
    {
        var cache = new Mock<ICacheService>();
        var registry = CreateRegistry(cache);

        await registry.AddAsync(Target, "ada");

        cache.Verify(
            c => c.SetBitsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<long>>()),
            Times.Never);
    }

    [Fact]
    public async Task AddAsync_ShouldWriteBitsIntoTheActiveGeneration()
    {
        var cache = CreateCacheWithGeneration(4, out var descriptor);
        var registry = CreateRegistry(cache);
        await registry.RefreshAsync();

        await registry.AddAsync(Target, "ada");

        cache.Verify(
            c => c.SetBitsAsync(
                "bloom:username:bits:4",
                It.Is<IReadOnlyCollection<long>>(p => p.Count == descriptor.HashCount)),
            Times.Once);
    }

    [Fact]
    public async Task AddAsync_ShouldObserveCancellation()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => registry.AddAsync(Target, "ada", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RefreshAsync_ShouldStayUnavailable_WhenNoGenerationHasBeenPublished()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("bloom:username:generation")).ReturnsAsync((string?)null);
        var registry = CreateRegistry(cache);

        await registry.RefreshAsync();

        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.Unavailable);
    }

    /// <summary>
    /// The single most important degradation rule: a missing bitmap must never be treated as an
    /// empty one, or the filter would report every existing username as free.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_ShouldNotTreatAMissingBitmapAsEmpty()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("bloom:username:generation")).ReturnsAsync("7");
        cache.Setup(c => c.GetBitmapAsync("bloom:username:bits:7")).ReturnsAsync((byte[]?)null);
        var registry = CreateRegistry(cache);

        await registry.RefreshAsync();

        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.Unavailable);
    }

    [Fact]
    public async Task RefreshAsync_ShouldAdoptAPublishedGeneration()
    {
        var cache = CreateCacheWithGeneration(3, out _, "ada");
        var registry = CreateRegistry(cache);

        await registry.RefreshAsync();

        registry.IsReady(Target).Should().BeTrue();
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
        registry.MightContain(Target, "not-present-anywhere").Should().Be(BloomFilterLookup.DefinitelyAbsent);
    }

    [Fact]
    public async Task RefreshAsync_ShouldKeepLocalWrites_WhenAdoptingANewGeneration()
    {
        // A rebuild that started before this write must not drop it.
        var cache = CreateCacheWithGeneration(5, out _);
        var registry = CreateRegistry(cache);
        await registry.AddAsync(Target, "written-locally");

        await registry.RefreshAsync();

        registry.MightContain(Target, "written-locally").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RefreshAsync_ShouldReplayPendingValues_WhenAdoptingANewGeneration()
    {
        // Written by another instance into the previous generation, after the rebuild snapshot.
        var cache = CreateCacheWithGeneration(2, out _);
        cache.Setup(c => c.SetMembersAsync("bloom:username:pending")).ReturnsAsync(["other-instance", ""]);
        var registry = CreateRegistry(cache);

        await registry.RefreshAsync();

        registry.MightContain(Target, "other-instance").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RefreshAsync_ShouldMergeSharedBits_WithinTheSameGeneration()
    {
        var cache = CreateCacheWithGeneration(9, out _);
        var registry = CreateRegistry(cache);
        await registry.RefreshAsync();

        cache.Setup(c => c.GetBitmapAsync("bloom:username:bits:9"))
            .ReturnsAsync(BuildBitmapBytes("added-elsewhere"));

        await registry.RefreshAsync();

        registry.MightContain(Target, "added-elsewhere").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public async Task RefreshAsync_ShouldSurviveACacheFailure()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("down"));
        var registry = CreateRegistry(cache);

        var act = () => registry.RefreshAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RefreshAsync_ShouldObserveCancellation()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => registry.RefreshAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishGenerationAsync_ShouldWriteBitsBeforeMovingThePointer()
    {
        var sequence = new List<string>();
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true)
            .Callback<string, byte[], TimeSpan?>((key, _, _) => sequence.Add($"bitmap:{key}"));
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true)
            .Callback<string, string, TimeSpan?>((key, _, _) => sequence.Add($"pointer:{key}"));
        var registry = CreateRegistry(cache);

        var published = await registry.PublishGenerationAsync(Target, BuildBitmap("ada"), 2);

        published.Should().BeTrue();
        sequence.Should().Equal("bitmap:bloom:username:bits:2", "pointer:bloom:username:generation");
    }

    [Fact]
    public async Task PublishGenerationAsync_ShouldExpireTheSupersededGeneration_RatherThanDeleteIt()
    {
        var cache = CreatePublishableCache();
        var registry = CreateRegistry(cache);

        await registry.PublishGenerationAsync(Target, BuildBitmap("ada"), 3);

        // Another instance may still be writing into it until it notices the flip.
        cache.Verify(
            c => c.SetExpiryAsync("bloom:username:bits:2", It.IsAny<TimeSpan>()),
            Times.Once);
        cache.Verify(c => c.DeleteKeyAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PublishGenerationAsync_ShouldNotExpireAnything_ForTheFirstGeneration()
    {
        var cache = CreatePublishableCache();
        var registry = CreateRegistry(cache);

        await registry.PublishGenerationAsync(Target, BuildBitmap("ada"), 1);

        cache.Verify(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task PublishGenerationAsync_ShouldReportFailure_WhenSharedStateRejectsTheWrite()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);
        var registry = CreateRegistry(cache);

        (await registry.PublishGenerationAsync(Target, BuildBitmap("ada"), 1)).Should().BeFalse();
    }

    [Fact]
    public async Task PublishGenerationAsync_ShouldReportFailure_WhenThePointerCannotMove()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(false);
        var registry = CreateRegistry(cache);

        (await registry.PublishGenerationAsync(Target, BuildBitmap("ada"), 1)).Should().BeFalse();
    }

    [Fact]
    public async Task ReadGenerationAsync_ShouldReturnZero_ForMissingOrUnparseableValues()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("bloom:username:generation")).ReturnsAsync("not-a-number");
        var registry = CreateRegistry(cache);

        (await registry.ReadGenerationAsync(Target)).Should().Be(0);
    }

    [Fact]
    public void InstallLocal_ShouldMakeTheFilterReadable_WithoutSharedState()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());

        registry.InstallLocal(Target, BuildBitmap("ada"));

        registry.IsReady(Target).Should().BeTrue();
        registry.MightContain(Target, "ada").Should().Be(BloomFilterLookup.PossiblyPresent);
    }

    [Fact]
    public void InstallLocal_ShouldIgnoreAnUnknownTarget()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());

        var act = () => registry.InstallLocal("email", BuildBitmap("ada"));

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ConsumeSharedStateDirty_ShouldLatchAFailedSharedWriteExactlyOnce()
    {
        var cache = CreateCacheWithGeneration(1, out _);
        cache.Setup(c => c.SetBitsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(false);
        var registry = CreateRegistry(cache);
        await registry.RefreshAsync();

        registry.ConsumeSharedStateDirty().Should().BeFalse();
        await registry.AddAsync(Target, "ada");

        registry.ConsumeSharedStateDirty().Should().BeTrue();
        registry.ConsumeSharedStateDirty().Should().BeFalse();
    }

    [Fact]
    public async Task ConsumeSharedStateDirty_ShouldStayClean_WhenSharedWritesSucceed()
    {
        var cache = CreateCacheWithGeneration(1, out _);
        var registry = CreateRegistry(cache);
        await registry.RefreshAsync();

        await registry.AddAsync(Target, "ada");

        registry.ConsumeSharedStateDirty().Should().BeFalse();
    }

    [Fact]
    public void Targets_ShouldExposeTheConfiguredFilters()
    {
        CreateRegistry(new Mock<ICacheService>()).Targets.Should().Equal(Target);
    }

    [Fact]
    public void GetDescriptor_ShouldReturnNull_ForAnUnknownTarget()
    {
        CreateRegistry(new Mock<ICacheService>()).GetDescriptor("email").Should().BeNull();
    }

    [Fact]
    public void GetStats_ShouldReturnNull_ForAnUnknownTarget()
    {
        CreateRegistry(new Mock<ICacheService>()).GetStats("email").Should().BeNull();
    }

    [Fact]
    public async Task GetStats_ShouldTrackOccupancy()
    {
        var registry = CreateRegistry(new Mock<ICacheService>());
        var descriptor = registry.GetDescriptor(Target)!;

        var empty = registry.GetStats(Target)!;
        empty.SetBits.Should().Be(0);
        empty.EstimatedFalsePositiveRate.Should().Be(0);
        empty.BitCount.Should().Be(descriptor.BitCount);
        empty.HashCount.Should().Be(descriptor.HashCount);
        empty.Target.Should().Be(Target);

        await registry.AddAsync(Target, "ada");

        registry.GetStats(Target)!.SetBits.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The property the whole design rests on: whatever the filter has been told about, it must
    /// never answer DefinitelyAbsent for. False positives are permitted and merely cost a query.
    /// </summary>
    [Fact]
    public async Task Registry_ShouldNeverProduceAFalseNegative()
    {
        var registry = CreateRegistry(new Mock<ICacheService>(), expectedItems: 5000);
        registry.InstallLocal(Target, new BloomBitmap(new BloomFilterDescriptor(Target, 5000, 0.01).BitCount));
        var random = new Random(20260905);
        var added = Enumerable.Range(0, 5000)
            .Select(_ => $"user-{random.Next():x8}-{random.Next():x8}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var value in added)
            await registry.AddAsync(Target, value);

        foreach (var value in added)
            registry.MightContain(Target, value).Should().Be(BloomFilterLookup.PossiblyPresent);

        var falsePositives = 0;
        const int probes = 20000;
        for (var i = 0; i < probes; i++)
        {
            var candidate = $"absent-{i}";
            if (added.Contains(candidate))
                continue;

            if (registry.MightContain(Target, candidate) == BloomFilterLookup.PossiblyPresent)
                falsePositives++;
        }

        ((double)falsePositives / probes).Should().BeLessThan(0.03);
    }

    private static BloomFilterRegistry CreateRegistry(Mock<ICacheService> cache, long expectedItems = 10_000)
    {
        var options = Options.Create(new BloomFilterOptions
        {
            Targets = new Dictionary<string, BloomFilterTargetOptions>(StringComparer.Ordinal)
            {
                [Target] = new() { ExpectedItems = expectedItems, FalsePositiveRate = 0.01 },
            },
        });

        return new BloomFilterRegistry(cache.Object, options, TimeProvider.System);
    }

    private static Mock<ICacheService> CreatePublishableCache()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetBitmapAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetExpiryAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
        return cache;
    }

    private static Mock<ICacheService> CreateCacheWithGeneration(
        long generation,
        out BloomFilterDescriptor descriptor,
        params string[] seeded)
    {
        descriptor = BuildDescriptor();

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetValueAsync("bloom:username:generation"))
            .ReturnsAsync(generation.ToString());
        cache.Setup(c => c.GetBitmapAsync($"bloom:username:bits:{generation}"))
            .ReturnsAsync(BuildBitmapBytes(seeded));
        cache.Setup(c => c.SetBitsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(true);
        cache.Setup(c => c.SetMembersAsync(It.IsAny<string>())).ReturnsAsync([]);
        return cache;
    }

    private static BloomFilterDescriptor BuildDescriptor(long expectedItems = 10_000) =>
        new(Target, expectedItems, 0.01);

    private static BloomBitmap BuildBitmap(params string[] values)
    {
        var descriptor = BuildDescriptor();
        var bitmap = new BloomBitmap(descriptor.BitCount);

        foreach (var value in values)
            bitmap.SetAll(descriptor.GetBitPositions(value));

        return bitmap;
    }

    private static byte[] BuildBitmapBytes(params string[] values) => BuildBitmap(values).ToBytes();
}
