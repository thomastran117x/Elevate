namespace backend.main.features.bloom;

/// <summary>
/// Supplies the authoritative set of values for one bloom filter target when it is rebuilt.
/// </summary>
/// <remarks>
/// This is the extension point for the remaining targets. To add club names or emails, register
/// another implementation against <see cref="BloomFilterTargets"/> and add a
/// <c>BloomFilters:Targets:&lt;name&gt;</c> configuration entry; nothing in the registry, the
/// rebuild service or the cache layer needs to change.
///
/// Implementations are resolved from a scope per rebuild, so they may depend on the database
/// context. Values must be yielded already normalised, using the same normaliser the read path
/// uses, or the rebuilt filter will not match lookups.
/// </remarks>
public interface IBloomFilterSource
{
    /// <summary>Which target this source populates. One of <see cref="BloomFilterTargets"/>.</summary>
    string Target
    {
        get;
    }

    /// <summary>
    /// Streams every value currently occupying the namespace. Streamed rather than materialised
    /// so a large table does not have to be held in memory alongside the bitmap being built.
    /// </summary>
    IAsyncEnumerable<string> EnumerateAsync(CancellationToken cancellationToken);
}
