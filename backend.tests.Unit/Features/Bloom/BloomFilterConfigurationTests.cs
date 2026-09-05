using System.ComponentModel.DataAnnotations;

using backend.main.features.bloom;
using backend.main.shared.probabilistic;

using FluentAssertions;

namespace backend.tests.Unit.Features.Bloom;

public class BloomFilterConfigurationTests
{
    [Fact]
    public void Descriptor_ShouldDeriveWidthAndHashCountFromTheTargetRate()
    {
        var descriptor = new BloomFilterDescriptor(BloomFilterTargets.Username, 100_000, 0.01);

        descriptor.Target.Should().Be(BloomFilterTargets.Username);
        descriptor.ExpectedItems.Should().Be(100_000);
        descriptor.FalsePositiveRate.Should().Be(0.01);
        descriptor.BitCount.Should().Be(BloomFilterMath.OptimalBitCount(100_000, 0.01));
        descriptor.HashCount.Should().Be(BloomFilterMath.OptimalHashCount(descriptor.BitCount, 100_000));
        descriptor.ByteCount.Should().Be((int)((descriptor.BitCount + 7) / 8));
    }

    [Fact]
    public void Descriptor_ShouldRejectAMissingTarget()
    {
        var act = () => new BloomFilterDescriptor("", 1000, 0.01);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Descriptor_ShouldProduceBitPositionsInsideItsOwnWidth()
    {
        var descriptor = new BloomFilterDescriptor(BloomFilterTargets.Username, 1000, 0.01);

        var positions = descriptor.GetBitPositions("ada");

        positions.Should().HaveCount(descriptor.HashCount);
        positions.Should().OnlyContain(p => p >= 0 && p < descriptor.BitCount);
    }

    [Fact]
    public void Descriptor_ShouldBeBuildableFromOptions()
    {
        var descriptor = BloomFilterDescriptor.FromOptions(
            BloomFilterTargets.Email,
            new BloomFilterTargetOptions { ExpectedItems = 500, FalsePositiveRate = 0.02 });

        descriptor.Target.Should().Be(BloomFilterTargets.Email);
        descriptor.ExpectedItems.Should().Be(500);
    }

    [Fact]
    public void Options_ShouldDefaultToTheUsernameTarget()
    {
        new BloomFilterOptions().Targets.Should().ContainKey(BloomFilterTargets.Username);
    }

    [Fact]
    public void Options_ShouldAcceptEveryKnownTarget()
    {
        var options = new BloomFilterOptions
        {
            Targets = BloomFilterTargets.All.ToDictionary(name => name, _ => new BloomFilterTargetOptions()),
        };

        Validate(options).Should().BeEmpty();
    }

    [Fact]
    public void Options_ShouldRejectAnUnknownTarget()
    {
        var options = new BloomFilterOptions
        {
            Targets = new Dictionary<string, BloomFilterTargetOptions> { ["nickname"] = new() },
        };

        Validate(options).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("nickname");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2_000_000_000)]
    public void Options_ShouldRejectAnImplausibleCapacity(long expectedItems)
    {
        var options = BuildOptions(new BloomFilterTargetOptions { ExpectedItems = expectedItems });

        Validate(options).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("ExpectedItems");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void Options_ShouldRejectAnImplausibleFalsePositiveRate(double rate)
    {
        var options = BuildOptions(new BloomFilterTargetOptions { FalsePositiveRate = rate });

        Validate(options).Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("FalsePositiveRate");
    }

    [Fact]
    public void Targets_ShouldReserveNamesForTheRemainingFilters()
    {
        // Club names and emails are not registered yet, but the names are fixed so their Redis
        // keys and hash domains cannot drift once they are.
        BloomFilterTargets.All.Should().BeEquivalentTo(["username", "club-name", "email"]);
    }

    private static BloomFilterOptions BuildOptions(BloomFilterTargetOptions target) =>
        new()
        {
            Targets = new Dictionary<string, BloomFilterTargetOptions>(StringComparer.Ordinal)
            {
                [BloomFilterTargets.Username] = target,
            },
        };

    private static List<ValidationResult> Validate(BloomFilterOptions options) =>
        [.. options.Validate(new ValidationContext(options))];
}
