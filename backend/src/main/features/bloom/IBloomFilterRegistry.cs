namespace backend.main.features.bloom;

/// <summary>
/// Result of a bloom filter lookup. The three cases are deliberately distinct: callers must not
/// be able to confuse "the filter proved this is absent" with "the filter could not answer".
/// </summary>
public enum BloomFilterLookup
{
    /// <summary>
    /// No filter is loaded for this target (disabled, not yet hydrated, or misconfigured).
    /// The caller must consult the database.
    /// </summary>
    Unavailable = 0,

    /// <summary>
    /// At least one of the value's bits is clear, so the value is definitely not in the set.
    /// This is the only answer that lets a caller skip the database, and it is exact.
    /// </summary>
    DefinitelyAbsent = 1,

    /// <summary>
    /// Every bit is set. The value is probably present, but this may be a collision, so the
    /// caller must confirm against the database.
    /// </summary>
    PossiblyPresent = 2,
}

/// <summary>
/// Read and write access to the process-local half of each bloom filter, plus the plumbing that
/// keeps it in step with the shared Redis bitmap.
/// </summary>
/// <remarks>
/// Singleton. Every value passed in must already be normalised by the same policy the target's
/// <see cref="IBloomFilterSource"/> uses — for usernames that is
/// <see cref="profile.UsernamePolicy.Normalize"/>.
/// </remarks>
public interface IBloomFilterRegistry
{
    /// <summary>True when a filter is configured and hydrated for this target.</summary>
    bool IsReady(string target);

    /// <summary>
    /// Tests a value against the local bitmap. Never touches the network, so this is safe on a
    /// request hot path.
    /// </summary>
    BloomFilterLookup MightContain(string target, string normalizedValue);

    /// <summary>
    /// Records a value as present, locally and in the shared bitmap. Call this after the write
    /// that claimed the value has committed. Adding a value that never commits only costs a
    /// future false positive; failing to add one that did commit is the error that matters.
    /// </summary>
    Task AddAsync(string target, string normalizedValue, CancellationToken cancellationToken = default);

    /// <summary>Merges the shared bitmap into the local one and adopts a new generation if one was published.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Every target that has a descriptor, whether or not it is hydrated.</summary>
    IReadOnlyCollection<string> Targets
    {
        get;
    }

    /// <summary>Current occupancy and estimated false-positive rate, for diagnostics and rebuild decisions.</summary>
    BloomFilterStats? GetStats(string target);
}

/// <param name="Target">Target name.</param>
/// <param name="Generation">Active generation number, or 0 when the filter is local-only.</param>
/// <param name="BitCount">Filter width.</param>
/// <param name="HashCount">Hash rounds per lookup.</param>
/// <param name="SetBits">Bits currently set.</param>
/// <param name="EstimatedFalsePositiveRate">Estimated rate at the current occupancy.</param>
public sealed record BloomFilterStats(
    string Target,
    long Generation,
    long BitCount,
    int HashCount,
    long SetBits,
    double EstimatedFalsePositiveRate);
