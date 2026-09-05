namespace backend.main.features.bloom;

/// <summary>
/// Registry used when the bloom feature flag is off. Every lookup reports
/// <see cref="BloomFilterLookup.Unavailable"/>, so callers fall through to the database and
/// behave exactly as they did before the filters existed.
/// </summary>
public sealed class DisabledBloomFilterRegistry : IBloomFilterRegistry
{
    public IReadOnlyCollection<string> Targets => [];

    public bool IsReady(string target) => false;

    public BloomFilterLookup MightContain(string target, string normalizedValue) =>
        BloomFilterLookup.Unavailable;

    public Task AddAsync(string target, string normalizedValue, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public BloomFilterStats? GetStats(string target) => null;
}
