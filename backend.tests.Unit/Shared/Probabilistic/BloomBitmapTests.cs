using backend.main.shared.probabilistic;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Probabilistic;

public class BloomBitmapTests
{
    [Fact]
    public void Set_ShouldUseRedisBitOrdering_SoSharedBitmapsAgree()
    {
        var bitmap = new BloomBitmap(16);

        bitmap.Set(0);

        // Redis numbers bit 0 as the most significant bit of the first byte.
        bitmap.ToBytes()[0].Should().Be(0x80);
    }

    [Theory]
    [InlineData(0, 0, 0x80)]
    [InlineData(7, 0, 0x01)]
    [InlineData(8, 1, 0x80)]
    [InlineData(15, 1, 0x01)]
    public void Set_ShouldTargetTheExpectedByteAndMask(long position, int byteIndex, byte mask)
    {
        var bitmap = new BloomBitmap(16);

        bitmap.Set(position);

        bitmap.ToBytes()[byteIndex].Should().Be(mask);
    }

    [Fact]
    public void Get_ShouldReportOnlyBitsThatWereSet()
    {
        var bitmap = new BloomBitmap(64);
        bitmap.SetAll([3, 17, 44]);

        bitmap.Get(3).Should().BeTrue();
        bitmap.Get(17).Should().BeTrue();
        bitmap.Get(44).Should().BeTrue();
        bitmap.Get(4).Should().BeFalse();
    }

    [Fact]
    public void GetAll_ShouldRequireEveryPosition()
    {
        var bitmap = new BloomBitmap(64);
        bitmap.SetAll([1, 2, 3]);

        bitmap.GetAll([1, 2, 3]).Should().BeTrue();
        bitmap.GetAll([1, 2, 9]).Should().BeFalse();
    }

    [Fact]
    public void ByteCount_ShouldRoundUpToWholeBytes()
    {
        new BloomBitmap(1).ByteCount.Should().Be(1);
        new BloomBitmap(8).ByteCount.Should().Be(1);
        new BloomBitmap(9).ByteCount.Should().Be(2);
    }

    [Fact]
    public void Constructor_ShouldRejectANonPositiveWidth()
    {
        var act = () => new BloomBitmap(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)]
    public void Get_ShouldRejectPositionsOutsideTheFilter(long position)
    {
        var bitmap = new BloomBitmap(64);

        var get = () => bitmap.Get(position);
        var set = () => bitmap.Set(position);

        get.Should().Throw<ArgumentOutOfRangeException>();
        set.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetAll_ShouldRejectAnOutOfRangePosition()
    {
        var bitmap = new BloomBitmap(64);

        var act = () => bitmap.SetAll([1, 999]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FromBytes_ShouldRoundTrip()
    {
        var original = new BloomBitmap(128);
        original.SetAll([0, 5, 63, 127]);

        var restored = BloomBitmap.FromBytes(original.ToBytes(), 128);

        restored.GetAll([0, 5, 63, 127]).Should().BeTrue();
        restored.CountSetBits().Should().Be(4);
    }

    [Fact]
    public void FromBytes_ShouldZeroExtendAShortPayload()
    {
        // Redis SETBIT only grows a string to the highest bit touched, so a bitmap read back
        // is routinely shorter than the configured width.
        var restored = BloomBitmap.FromBytes([0x80], 128);

        restored.ByteCount.Should().Be(16);
        restored.Get(0).Should().BeTrue();
        restored.Get(120).Should().BeFalse();
    }

    [Fact]
    public void FromBytes_ShouldTruncateAnOversizedPayload()
    {
        var restored = BloomBitmap.FromBytes(new byte[64], 32);

        restored.ByteCount.Should().Be(4);
    }

    [Fact]
    public void FromBytes_ShouldRejectANonPositiveWidth()
    {
        var act = () => BloomBitmap.FromBytes([0x01], 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UnionWith_ShouldAddBitsAndNeverClearThem()
    {
        var left = new BloomBitmap(64);
        left.SetAll([1, 2]);

        var right = new BloomBitmap(64);
        right.SetAll([2, 40]);

        left.UnionWith(right);

        left.GetAll([1, 2, 40]).Should().BeTrue();
        left.CountSetBits().Should().Be(3);
    }

    [Fact]
    public void UnionWith_ShouldRejectNull()
    {
        var act = () => new BloomBitmap(64).UnionWith(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CountSetBits_ShouldStartAtZero()
    {
        new BloomBitmap(1024).CountSetBits().Should().Be(0);
    }

    [Fact]
    public void ToBytes_ShouldReturnACopy_SoCallersCannotMutateTheFilter()
    {
        var bitmap = new BloomBitmap(64);

        var snapshot = bitmap.ToBytes();
        snapshot[0] = 0xFF;

        bitmap.Get(0).Should().BeFalse();
    }

    /// <summary>
    /// Setting a bit is a read-modify-write over a shared byte. If concurrent writers can lose
    /// one another's update, a bit that should be 1 stays 0 — which is a false negative, the one
    /// error direction the filter is not allowed to produce. Small width forces byte contention.
    /// </summary>
    [Fact]
    public async Task Set_ShouldNotLoseConcurrentUpdates()
    {
        var bitmap = new BloomBitmap(512);
        var positions = Enumerable.Range(0, 512).Select(i => (long)i).ToArray();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            foreach (var position in positions)
                bitmap.Set(position);
        })));

        bitmap.CountSetBits().Should().Be(512);
    }
}
