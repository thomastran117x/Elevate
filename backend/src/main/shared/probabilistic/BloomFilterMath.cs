namespace backend.main.shared.probabilistic;

/// <summary>
/// Sizing math for a classic bloom filter: given the number of items expected and the
/// false-positive rate that can be tolerated, derive the bit count and hash count.
/// </summary>
public static class BloomFilterMath
{
    /// <summary>Smallest filter we will ever allocate, so a misconfigured capacity cannot produce a 0-bit filter.</summary>
    public const long MinBitCount = 1024;

    /// <summary>Upper bound on hash rounds. Beyond this the cost per lookup outweighs the accuracy gained.</summary>
    public const int MaxHashCount = 16;

    private static readonly double Ln2 = Math.Log(2);
    private static readonly double Ln2Squared = Ln2 * Ln2;

    /// <summary>
    /// Optimal bit count: m = -n * ln(p) / (ln 2)^2.
    /// </summary>
    /// <param name="expectedItems">Number of distinct values the filter is sized for.</param>
    /// <param name="falsePositiveRate">Target false-positive rate, exclusive between 0 and 1.</param>
    public static long OptimalBitCount(long expectedItems, double falsePositiveRate)
    {
        ValidateInputs(expectedItems, falsePositiveRate);

        var bits = -expectedItems * Math.Log(falsePositiveRate) / Ln2Squared;
        var rounded = (long)Math.Ceiling(bits);

        // Round up to a whole byte so the local BitArray and the Redis bitmap describe the
        // same amount of storage, and the byte-level merge on rebuild never truncates.
        rounded = (rounded + 7) / 8 * 8;

        return Math.Max(MinBitCount, rounded);
    }

    /// <summary>
    /// Optimal hash count: k = (m / n) * ln 2, clamped to at least one round.
    /// </summary>
    public static int OptimalHashCount(long bitCount, long expectedItems)
    {
        if (bitCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitCount), "Bit count must be positive.");
        if (expectedItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedItems), "Expected items must be positive.");

        var hashes = (double)bitCount / expectedItems * Ln2;
        var rounded = (int)Math.Round(hashes, MidpointRounding.AwayFromZero);

        return Math.Clamp(rounded, 1, MaxHashCount);
    }

    /// <summary>
    /// Expected false-positive rate for a filter of <paramref name="bitCount"/> bits and
    /// <paramref name="hashCount"/> hashes once <paramref name="itemCount"/> items are present:
    /// (1 - e^(-k*n/m))^k. Used for diagnostics and to decide when a rebuild is overdue.
    /// </summary>
    public static double EstimateFalsePositiveRate(long bitCount, int hashCount, long itemCount)
    {
        if (bitCount <= 0 || hashCount <= 0)
            return 1;

        if (itemCount <= 0)
            return 0;

        var exponent = -(double)hashCount * itemCount / bitCount;
        return Math.Pow(1 - Math.Exp(exponent), hashCount);
    }

    private static void ValidateInputs(long expectedItems, double falsePositiveRate)
    {
        if (expectedItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(expectedItems), "Expected items must be positive.");

        if (double.IsNaN(falsePositiveRate) || falsePositiveRate <= 0 || falsePositiveRate >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(falsePositiveRate),
                "False-positive rate must be between 0 and 1, exclusive.");
        }
    }
}
