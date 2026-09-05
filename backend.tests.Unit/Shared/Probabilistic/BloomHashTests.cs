using backend.main.shared.probabilistic;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Probabilistic;

public class BloomHashTests
{
    /// <summary>
    /// Locks the value-to-bit mapping to a fixed vector, independently derived from
    /// <c>SHA256("username:thomas")</c> = 2a00bea5...; reading the first two little-endian
    /// 64-bit words gives h1 = 0xFE562CB0A5BE002A and h2 = 0xF57EEA184AC8D41F (odd-forced),
    /// so positions modulo 1024 are 42 and 73.
    /// </summary>
    /// <remarks>
    /// This test exists to fail loudly if anyone swaps the hash for something cheaper. The bits
    /// live in a Redis bitmap shared across processes, restarts and deploys, so a hash change is
    /// not an optimisation — it silently invalidates every stored filter in the false-negative
    /// direction. Changing it requires rebuilding every generation, not editing this vector.
    /// </remarks>
    [Fact]
    public void GetBitPositions_ShouldBeStableAcrossProcesses()
    {
        var positions = BloomHash.GetBitPositions("username", "thomas", 1024, 2);

        positions.Should().Equal(42L, 73L);
    }

    [Fact]
    public void GetBitPositions_ShouldBeDeterministic()
    {
        var first = BloomHash.GetBitPositions("username", "ada", 4096, 7);
        var second = BloomHash.GetBitPositions("username", "ada", 4096, 7);

        first.Should().Equal(second);
    }

    [Fact]
    public void GetBitPositions_ShouldSeparateTargets_SoFiltersStayIndependent()
    {
        var username = BloomHash.GetBitPositions("username", "acme", 65536, 7);
        var clubName = BloomHash.GetBitPositions("club-name", "acme", 65536, 7);

        username.Should().NotEqual(clubName);
    }

    [Fact]
    public void GetBitPositions_ShouldReturnOneEntryPerHashRound()
    {
        BloomHash.GetBitPositions("username", "ada", 4096, 9).Should().HaveCount(9);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(1_000_003)]
    public void GetBitPositions_ShouldStayInsideTheFilter(long bitCount)
    {
        for (var i = 0; i < 250; i++)
        {
            var positions = BloomHash.GetBitPositions("username", $"user-{i}", bitCount, 7);

            positions.Should().OnlyContain(position => position >= 0 && position < bitCount);
        }
    }

    [Fact]
    public void GetBitPositions_ShouldNotCollapseToASinglePosition()
    {
        // An even stride can revisit the same bit and quietly weaken the filter; h2 is forced odd
        // to prevent it. Distinctness across rounds is the observable consequence.
        var positions = BloomHash.GetBitPositions("username", "ada", 1 << 20, 8);

        positions.Distinct().Should().HaveCount(8);
    }

    [Fact]
    public void GetBitPositions_ShouldHandleLongAndUnicodeValues()
    {
        var act = () => BloomHash.GetBitPositions("username", new string('é', 400), 4096, 7);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetBitPositions_ShouldAcceptAnEmptyValue()
    {
        BloomHash.GetBitPositions("username", string.Empty, 4096, 3).Should().HaveCount(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void GetBitPositions_ShouldRejectAMissingTarget(string? target)
    {
        var act = () => BloomHash.GetBitPositions(target!, "ada", 1024, 3);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetBitPositions_ShouldRejectANullValue()
    {
        var act = () => BloomHash.GetBitPositions("username", null!, 1024, 3);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(1024, 0)]
    [InlineData(1024, -2)]
    public void GetBitPositions_ShouldRejectDegenerateParameters(long bitCount, int hashCount)
    {
        var act = () => BloomHash.GetBitPositions("username", "ada", bitCount, hashCount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
