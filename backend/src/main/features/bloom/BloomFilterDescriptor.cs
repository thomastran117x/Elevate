using backend.main.shared.probabilistic;

namespace backend.main.features.bloom;

/// <summary>
/// The resolved shape of one target's filter: how wide it is, how many hash rounds it uses and
/// what it was sized for. Derived once at startup from <see cref="BloomFilterTargetOptions"/>.
/// </summary>
public sealed class BloomFilterDescriptor
{
    public BloomFilterDescriptor(string target, long expectedItems, double falsePositiveRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);

        Target = target;
        ExpectedItems = expectedItems;
        FalsePositiveRate = falsePositiveRate;
        BitCount = BloomFilterMath.OptimalBitCount(expectedItems, falsePositiveRate);
        HashCount = BloomFilterMath.OptimalHashCount(BitCount, expectedItems);
    }

    public string Target
    {
        get;
    }

    public long ExpectedItems
    {
        get;
    }

    public double FalsePositiveRate
    {
        get;
    }

    public long BitCount
    {
        get;
    }

    public int HashCount
    {
        get;
    }

    public int ByteCount => (int)((BitCount + 7) / 8);

    public static BloomFilterDescriptor FromOptions(string target, BloomFilterTargetOptions options) =>
        new(target, options.ExpectedItems, options.FalsePositiveRate);

    public long[] GetBitPositions(string normalizedValue) =>
        BloomHash.GetBitPositions(Target, normalizedValue, BitCount, HashCount);
}
