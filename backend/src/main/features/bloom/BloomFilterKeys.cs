namespace backend.main.features.bloom;

/// <summary>
/// Redis key layout for the shared half of the two-tier filters.
/// </summary>
internal static class BloomFilterKeys
{
    private const string Prefix = "bloom";

    /// <summary>Pointer to the active generation number for a target.</summary>
    public static string Generation(string target) => $"{Prefix}:{target}:generation";

    /// <summary>Bitmap for one generation. Generations are never mutated after they are retired.</summary>
    public static string Bits(string target, long generation) => $"{Prefix}:{target}:bits:{generation}";

    /// <summary>
    /// Values written by any instance since the last rebuild. Replayed onto a freshly built
    /// generation so a value added while the rebuild was reading the database is not dropped.
    /// </summary>
    public static string Pending(string target) => $"{Prefix}:{target}:pending";

    /// <summary>Guards rebuilds so only one instance reads the whole table at a time.</summary>
    public static string RebuildLock(string target) => $"{Prefix}:{target}:rebuild-lock";
}
