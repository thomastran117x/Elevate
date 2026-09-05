using backend.main.features.bloom;

using FluentAssertions;

namespace backend.tests.Unit.Features.Bloom;

public class DisabledBloomFilterRegistryTests
{
    /// <summary>
    /// With the feature off every caller must behave exactly as it did before the filters
    /// existed, which means never receiving an answer it would act on.
    /// </summary>
    [Fact]
    public void MightContain_ShouldAlwaysReportUnavailable()
    {
        var registry = new DisabledBloomFilterRegistry();

        registry.MightContain(BloomFilterTargets.Username, "ada")
            .Should().Be(BloomFilterLookup.Unavailable);
        registry.MightContain(BloomFilterTargets.ClubName, "acme")
            .Should().Be(BloomFilterLookup.Unavailable);
    }

    [Fact]
    public async Task Registry_ShouldAcceptWritesWithoutRecordingAnything()
    {
        var registry = new DisabledBloomFilterRegistry();

        await registry.AddAsync(BloomFilterTargets.Username, "ada");
        await registry.RefreshAsync();

        registry.IsReady(BloomFilterTargets.Username).Should().BeFalse();
        registry.Targets.Should().BeEmpty();
        registry.GetStats(BloomFilterTargets.Username).Should().BeNull();
    }
}
