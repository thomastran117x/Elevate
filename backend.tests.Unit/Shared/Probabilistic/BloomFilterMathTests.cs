using backend.main.shared.probabilistic;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Probabilistic;

public class BloomFilterMathTests
{
    [Theory]
    [InlineData(1000, 0.01)]
    [InlineData(100_000, 0.01)]
    [InlineData(250_000, 0.001)]
    public void OptimalBitCount_ShouldMatchTheClosedForm(long items, double rate)
    {
        var expected = (long)Math.Ceiling(-items * Math.Log(rate) / (Math.Log(2) * Math.Log(2)));
        expected = (expected + 7) / 8 * 8;

        BloomFilterMath.OptimalBitCount(items, rate).Should().Be(expected);
    }

    [Fact]
    public void OptimalBitCount_ShouldReturnWholeBytes_SoLocalAndRedisAgreeOnWidth()
    {
        for (var items = 1; items <= 200; items++)
            (BloomFilterMath.OptimalBitCount(items, 0.01) % 8).Should().Be(0);
    }

    [Fact]
    public void OptimalBitCount_ShouldNeverFallBelowTheFloor()
    {
        BloomFilterMath.OptimalBitCount(1, 0.5).Should().Be(BloomFilterMath.MinBitCount);
    }

    [Fact]
    public void OptimalBitCount_ShouldGrowAsTheRateTightens()
    {
        var loose = BloomFilterMath.OptimalBitCount(100_000, 0.1);
        var tight = BloomFilterMath.OptimalBitCount(100_000, 0.001);

        tight.Should().BeGreaterThan(loose);
    }

    [Theory]
    [InlineData(0, 0.01)]
    [InlineData(-5, 0.01)]
    public void OptimalBitCount_ShouldRejectNonPositiveItemCounts(long items, double rate)
    {
        var act = () => BloomFilterMath.OptimalBitCount(items, rate);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void OptimalBitCount_ShouldRejectRatesOutsideTheOpenUnitInterval(double rate)
    {
        var act = () => BloomFilterMath.OptimalBitCount(1000, rate);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OptimalHashCount_ShouldMatchTheClosedForm()
    {
        var bits = BloomFilterMath.OptimalBitCount(100_000, 0.01);

        var expected = (int)Math.Round((double)bits / 100_000 * Math.Log(2), MidpointRounding.AwayFromZero);

        BloomFilterMath.OptimalHashCount(bits, 100_000).Should().Be(expected);
    }

    [Fact]
    public void OptimalHashCount_ShouldStayWithinItsClamp()
    {
        BloomFilterMath.OptimalHashCount(64, 100_000).Should().Be(1);
        BloomFilterMath.OptimalHashCount(long.MaxValue / 2, 1).Should().Be(BloomFilterMath.MaxHashCount);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(1024, 0)]
    [InlineData(1024, -3)]
    public void OptimalHashCount_ShouldRejectNonPositiveInputs(long bits, long items)
    {
        var act = () => BloomFilterMath.OptimalHashCount(bits, items);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EstimateFalsePositiveRate_ShouldBeZero_WhenNothingHasBeenAdded()
    {
        BloomFilterMath.EstimateFalsePositiveRate(1024, 7, 0).Should().Be(0);
    }

    [Fact]
    public void EstimateFalsePositiveRate_ShouldApproachTheTarget_AtTheSizedCapacity()
    {
        var bits = BloomFilterMath.OptimalBitCount(100_000, 0.01);
        var hashes = BloomFilterMath.OptimalHashCount(bits, 100_000);

        var estimate = BloomFilterMath.EstimateFalsePositiveRate(bits, hashes, 100_000);

        estimate.Should().BeApproximately(0.01, 0.005);
    }

    [Fact]
    public void EstimateFalsePositiveRate_ShouldRiseAsTheFilterFills()
    {
        var bits = BloomFilterMath.OptimalBitCount(10_000, 0.01);
        var hashes = BloomFilterMath.OptimalHashCount(bits, 10_000);

        var atCapacity = BloomFilterMath.EstimateFalsePositiveRate(bits, hashes, 10_000);
        var overloaded = BloomFilterMath.EstimateFalsePositiveRate(bits, hashes, 100_000);

        overloaded.Should().BeGreaterThan(atCapacity);
    }

    [Fact]
    public void EstimateFalsePositiveRate_ShouldSaturate_ForDegenerateParameters()
    {
        BloomFilterMath.EstimateFalsePositiveRate(0, 7, 10).Should().Be(1);
        BloomFilterMath.EstimateFalsePositiveRate(1024, 0, 10).Should().Be(1);
    }
}
