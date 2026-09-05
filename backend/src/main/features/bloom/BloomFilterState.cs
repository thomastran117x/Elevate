using backend.main.shared.probabilistic;

namespace backend.main.features.bloom;

/// <summary>
/// Mutable per-target state: the local bitmap that answers lookups, which shared generation it
/// came from, and the values this instance added recently.
/// </summary>
internal sealed class BloomFilterState
{
    private readonly object _swapGate = new();
    private readonly Queue<RecentAddition> _recent = new();

    private BloomBitmap _bitmap;
    private long _generation;
    private bool _ready;

    public BloomFilterState(BloomFilterDescriptor descriptor)
    {
        Descriptor = descriptor;
        _bitmap = new BloomBitmap(descriptor.BitCount);
    }

    public BloomFilterDescriptor Descriptor
    {
        get;
    }

    /// <summary>
    /// Volatile read: lookups take the current bitmap reference without a lock, so a generation
    /// swap is seen atomically as either the old or the new map, never a half-built one.
    /// </summary>
    public BloomBitmap Bitmap => Volatile.Read(ref _bitmap);

    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>False until the first hydration completes, so lookups report Unavailable rather than guessing.</summary>
    public bool IsReady => Volatile.Read(ref _ready);

    public void MarkReady() => Volatile.Write(ref _ready, true);

    public void RecordRecent(string value, DateTimeOffset addedAt, TimeSpan window)
    {
        lock (_recent)
        {
            _recent.Enqueue(new RecentAddition(value, addedAt));
            TrimRecent(addedAt - window);
        }
    }

    /// <summary>
    /// Installs a bitmap loaded from a shared generation, then replays this instance's recent
    /// additions onto it. Without the replay, a rebuild that began before a local write landed
    /// would drop that value and the filter would answer "absent" for a name that exists.
    /// </summary>
    public void AdoptGeneration(BloomBitmap bitmap, long generation, DateTimeOffset now, TimeSpan replayWindow)
    {
        lock (_swapGate)
        {
            List<RecentAddition> replay;
            lock (_recent)
            {
                TrimRecent(now - replayWindow);
                replay = [.. _recent];
            }

            foreach (var addition in replay)
                bitmap.SetAll(Descriptor.GetBitPositions(addition.Value));

            Volatile.Write(ref _bitmap, bitmap);
            Interlocked.Exchange(ref _generation, generation);
            Volatile.Write(ref _ready, true);
        }
    }

    /// <summary>Merges shared bits into the local map without changing generation. Always safe: union only adds bits.</summary>
    public void MergeShared(BloomBitmap shared)
    {
        lock (_swapGate)
        {
            Volatile.Read(ref _bitmap).UnionWith(shared);
            Volatile.Write(ref _ready, true);
        }
    }

    public BloomFilterStats GetStats()
    {
        var bitmap = Bitmap;
        var setBits = bitmap.CountSetBits();

        // Invert the occupancy back into an item estimate so the reported rate reflects what the
        // filter actually holds rather than what it was sized for.
        var estimatedItems = EstimateItemCount(setBits, bitmap.BitCount, Descriptor.HashCount);

        return new BloomFilterStats(
            Descriptor.Target,
            Generation,
            bitmap.BitCount,
            Descriptor.HashCount,
            setBits,
            BloomFilterMath.EstimateFalsePositiveRate(bitmap.BitCount, Descriptor.HashCount, estimatedItems));
    }

    private static long EstimateItemCount(long setBits, long bitCount, int hashCount)
    {
        if (setBits <= 0)
            return 0;

        if (setBits >= bitCount)
            return long.MaxValue / 2;

        // n ~= -(m/k) * ln(1 - X/m), the standard cardinality estimate for a bloom filter.
        var ratio = 1 - ((double)setBits / bitCount);
        var estimate = -((double)bitCount / hashCount) * Math.Log(ratio);

        return (long)Math.Max(0, Math.Round(estimate));
    }

    /// <summary>Caller must hold the <c>_recent</c> lock.</summary>
    private void TrimRecent(DateTimeOffset cutoff)
    {
        while (_recent.Count > 0 && _recent.Peek().AddedAt < cutoff)
            _recent.Dequeue();
    }

    private readonly record struct RecentAddition(string Value, DateTimeOffset AddedAt);
}
