using System.Globalization;

using backend.main.features.cache;
using backend.main.shared.probabilistic;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

namespace backend.main.features.bloom;

/// <summary>
/// Two-tier bloom filter registry: a process-local bitmap answers every lookup, and a Redis
/// bitmap carries writes between instances and across restarts.
/// </summary>
/// <remarks>
/// The safety property this type must preserve is one-directional. A filter may report
/// <see cref="BloomFilterLookup.PossiblyPresent"/> for a value that does not exist — that costs
/// one database query. It must never report <see cref="BloomFilterLookup.DefinitelyAbsent"/> for
/// a value that does, because callers skip the database on that answer. Every operation here is
/// therefore either "add bits" or "union bitmaps"; bits are only ever cleared by replacing the
/// whole generation with one rebuilt from the database.
///
/// The database remains the sole authority. The unique indexes on Users.Username and
/// UsernameReservations.Username, and the serializable transaction in
/// <c>AuthUserRepository.ChangeUsernameAsync</c>, are what actually prevent a duplicate; this
/// filter only decides whether it is worth asking them.
/// </remarks>
public sealed class BloomFilterRegistry : IBloomFilterRegistry
{
    private readonly Dictionary<string, BloomFilterState> _states;
    private readonly ICacheService _cache;
    private readonly TimeProvider _clock;
    private readonly BloomFilterOptions _options;

    private bool _sharedStateDirty;

    public BloomFilterRegistry(
        ICacheService cache,
        IOptions<BloomFilterOptions> options,
        TimeProvider clock)
    {
        _cache = cache;
        _clock = clock;
        _options = options.Value;

        // Targets come from configuration, so registering a new one is a config entry plus an
        // IBloomFilterSource — no change to this type.
        _states = _options.Targets.ToDictionary(
            entry => entry.Key,
            entry => new BloomFilterState(BloomFilterDescriptor.FromOptions(entry.Key, entry.Value)),
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Targets => _states.Keys;

    private TimeSpan ReplayWindow => TimeSpan.FromMinutes(_options.LocalReplayWindowMinutes);

    public bool IsReady(string target) =>
        _states.TryGetValue(target, out var state) && state.IsReady;

    public BloomFilterLookup MightContain(string target, string normalizedValue)
    {
        if (string.IsNullOrEmpty(normalizedValue))
            return BloomFilterLookup.Unavailable;

        if (!_states.TryGetValue(target, out var state) || !state.IsReady)
            return BloomFilterLookup.Unavailable;

        var positions = state.Descriptor.GetBitPositions(normalizedValue);

        return state.Bitmap.GetAll(positions)
            ? BloomFilterLookup.PossiblyPresent
            : BloomFilterLookup.DefinitelyAbsent;
    }

    public async Task AddAsync(
        string target,
        string normalizedValue,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(normalizedValue) || !_states.TryGetValue(target, out var state))
            return;

        var positions = state.Descriptor.GetBitPositions(normalizedValue);

        // Local first and unconditionally: this instance must never answer "absent" for a value
        // it just wrote, even if Redis is unreachable.
        state.Bitmap.SetAll(positions);
        state.RecordRecent(normalizedValue, _clock.GetUtcNow(), ReplayWindow);

        var generation = state.Generation;
        if (generation > 0)
        {
            var written = await _cache.SetBitsAsync(BloomFilterKeys.Bits(target, generation), positions);

            // The local bitmap now holds a bit the shared one does not. This instance still
            // answers correctly, but any instance that later hydrates from Redis would inherit
            // the hole, so flag it for a rebuild.
            if (!written)
                Volatile.Write(ref _sharedStateDirty, true);
        }

        // Recorded regardless of generation so a rebuild that is mid-flight, or the first rebuild
        // on a cold Redis, replays this value onto the generation it publishes.
        await _cache.SetAddAsync(BloomFilterKeys.Pending(target), normalizedValue);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (target, state) in _states)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RefreshTargetAsync(target, state);
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, $"[BloomFilterRegistry] Failed to refresh '{target}' from shared state.");
            }
        }
    }

    public BloomFilterStats? GetStats(string target) =>
        _states.TryGetValue(target, out var state) ? state.GetStats() : null;

    internal BloomFilterDescriptor? GetDescriptor(string target) =>
        _states.TryGetValue(target, out var state) ? state.Descriptor : null;

    /// <summary>
    /// Installs a locally built bitmap without touching shared state. Used when Redis is
    /// unavailable, so the filter still works in single-instance and degraded deployments.
    /// </summary>
    internal void InstallLocal(string target, BloomBitmap bitmap)
    {
        if (!_states.TryGetValue(target, out var state))
            return;

        state.AdoptGeneration(bitmap, state.Generation, _clock.GetUtcNow(), ReplayWindow);
    }

    /// <summary>Reads the active generation number, or 0 when none has been published.</summary>
    internal async Task<long> ReadGenerationAsync(string target)
    {
        var raw = await _cache.GetValueAsync(BloomFilterKeys.Generation(target));

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var generation)
            ? generation
            : 0;
    }

    /// <summary>
    /// Publishes a rebuilt bitmap as the next generation and points the target at it. The bits
    /// are written before the pointer moves, so a reader that sees the new generation always
    /// finds a complete bitmap behind it.
    /// </summary>
    internal async Task<bool> PublishGenerationAsync(string target, BloomBitmap bitmap, long generation)
    {
        var stored = await _cache.SetBitmapAsync(BloomFilterKeys.Bits(target, generation), bitmap.ToBytes());
        if (!stored)
            return false;

        var pointerMoved = await _cache.SetValueAsync(
            BloomFilterKeys.Generation(target),
            generation.ToString(CultureInfo.InvariantCulture));

        if (!pointerMoved)
            return false;

        AdoptPublishedLocally(target, bitmap, generation);

        if (generation > 1)
        {
            // Leave the superseded bitmap in place briefly: another instance may still be
            // writing bits into it, and it will pick up the new generation on its next refresh.
            await _cache.SetExpiryAsync(
                BloomFilterKeys.Bits(target, generation - 1),
                TimeSpan.FromMinutes(_options.RetiredGenerationTtlMinutes));
        }

        return true;
    }

    /// <summary>
    /// Reads and clears the flag set when a write to the shared bitmap failed. A true result
    /// means the local and shared filters have diverged and only a rebuild can reconcile them.
    /// </summary>
    internal bool ConsumeSharedStateDirty()
    {
        lock (_states)
        {
            if (!Volatile.Read(ref _sharedStateDirty))
                return false;

            Volatile.Write(ref _sharedStateDirty, false);
            return true;
        }
    }

    private void AdoptPublishedLocally(string target, BloomBitmap bitmap, long generation)
    {
        if (_states.TryGetValue(target, out var state))
            state.AdoptGeneration(bitmap, generation, _clock.GetUtcNow(), ReplayWindow);
    }

    private async Task RefreshTargetAsync(string target, BloomFilterState state)
    {
        var sharedGeneration = await ReadGenerationAsync(target);
        if (sharedGeneration <= 0)
            return;

        var bytes = await _cache.GetBitmapAsync(BloomFilterKeys.Bits(target, sharedGeneration));
        if (bytes is null)
            return;

        var shared = BloomBitmap.FromBytes(bytes, state.Descriptor.BitCount);

        if (sharedGeneration != state.Generation)
        {
            // A rebuild replaced the filter. Take the new map wholesale rather than merging, so
            // bits shed by the rebuild stay shed; recent local writes are replayed by AdoptGeneration.
            //
            // Another instance may have written into the previous generation between the rebuild's
            // snapshot and this flip. Those writes are recorded in the pending set, so replaying it
            // here closes the window rather than leaving them missing until the next rebuild.
            foreach (var value in await _cache.SetMembersAsync(BloomFilterKeys.Pending(target)))
            {
                if (!string.IsNullOrEmpty(value))
                    shared.SetAll(state.Descriptor.GetBitPositions(value));
            }

            state.AdoptGeneration(shared, sharedGeneration, _clock.GetUtcNow(), ReplayWindow);
            return;
        }

        state.MergeShared(shared);
    }
}
