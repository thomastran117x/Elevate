using backend.main.features.cache;
using backend.main.shared.probabilistic;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

namespace backend.main.features.bloom;

/// <summary>
/// Rebuilds each registered filter from its authoritative source and publishes the result as a
/// new shared generation.
/// </summary>
/// <remarks>
/// A rebuild is the only operation that can clear a bit. Deleting a user or letting a username
/// reservation lapse frees a name, but a bloom filter has no delete, so those bits linger and
/// slowly inflate the false-positive rate. Rebuilding from the database and swapping the whole
/// bitmap is what returns the filter to its configured accuracy.
/// </remarks>
public sealed class BloomFilterRebuildRunner
{
    private readonly BloomFilterRegistry _registry;
    private readonly IEnumerable<IBloomFilterSource> _sources;
    private readonly ICacheService _cache;
    private readonly BloomFilterOptions _options;

    public BloomFilterRebuildRunner(
        BloomFilterRegistry registry,
        IEnumerable<IBloomFilterSource> sources,
        ICacheService cache,
        IOptions<BloomFilterOptions> options)
    {
        _registry = registry;
        _sources = sources;
        _cache = cache;
        _options = options.Value;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await RebuildAsync(source, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"[BloomFilterRebuildRunner] Rebuild failed for '{source.Target}'.");
            }
        }
    }

    internal async Task RebuildAsync(IBloomFilterSource source, CancellationToken cancellationToken)
    {
        var descriptor = _registry.GetDescriptor(source.Target);
        if (descriptor is null)
        {
            Logger.Warn($"[BloomFilterRebuildRunner] No filter configured for source target '{source.Target}'.");
            return;
        }

        // Built unconditionally rather than only by the instance that wins the lock: this is also
        // how a process hydrates at startup, and it must succeed even when Redis is unreachable.
        var bitmap = new BloomBitmap(descriptor.BitCount);
        long count = 0;

        await foreach (var value in source.EnumerateAsync(cancellationToken))
        {
            if (string.IsNullOrEmpty(value))
                continue;

            bitmap.SetAll(descriptor.GetBitPositions(value));
            count++;
        }

        // Values written by any instance while the table was being read. Replaying them closes
        // the window where a signup commits after the snapshot but before the pointer flips.
        var pending = await _cache.SetMembersAsync(BloomFilterKeys.Pending(source.Target));
        foreach (var value in pending)
        {
            if (!string.IsNullOrEmpty(value))
                bitmap.SetAll(descriptor.GetBitPositions(value));
        }

        await PublishOrInstallAsync(source.Target, bitmap, pending, count, descriptor);
    }

    private async Task PublishOrInstallAsync(
        string target,
        BloomBitmap bitmap,
        string[] replayed,
        long sourceCount,
        BloomFilterDescriptor descriptor)
    {
        var lockKey = BloomFilterKeys.RebuildLock(target);
        var lockToken = Guid.NewGuid().ToString("N");
        var acquired = await _cache.AcquireLockAsync(lockKey, lockToken, TimeSpan.FromMinutes(10));

        if (!acquired)
        {
            // Either another instance is publishing, or Redis is unavailable. Both are handled the
            // same way: keep the freshly built map locally and let the next refresh reconcile.
            _registry.InstallLocal(target, bitmap);
            Logger.Info(
                $"[BloomFilterRebuildRunner] Installed '{target}' locally without publishing "
                + $"({sourceCount} values, {bitmap.CountSetBits()}/{descriptor.BitCount} bits set).");
            return;
        }

        try
        {
            var nextGeneration = await _registry.ReadGenerationAsync(target) + 1;
            var published = await _registry.PublishGenerationAsync(target, bitmap, nextGeneration);

            if (!published)
            {
                _registry.InstallLocal(target, bitmap);
                Logger.Warn($"[BloomFilterRebuildRunner] Could not publish '{target}'; using local filter only.");
                return;
            }

            // A value committed between the first pending read and the pointer moving was written
            // into the *previous* generation, so it is absent from the bitmap just published. A
            // same-generation refresh only unions shared bits and never replays pending, so
            // without this second pass that name would read as definitely absent until the next
            // rebuild. Re-reading after the flip catches exactly those late arrivals.
            var lateArrivals = (await _cache.SetMembersAsync(BloomFilterKeys.Pending(target)))
                .Except(replayed, StringComparer.Ordinal)
                .Where(value => !string.IsNullOrEmpty(value))
                .ToArray();

            foreach (var value in lateArrivals)
            {
                var positions = descriptor.GetBitPositions(value);
                bitmap.SetAll(positions);
                await _cache.SetBitsAsync(BloomFilterKeys.Bits(target, nextGeneration), positions);
            }

            // Only the values actually folded into this generation are cleared. Anything arriving
            // after this point stays pending for the next rebuild to replay.
            foreach (var value in replayed.Concat(lateArrivals))
                await _cache.SetRemoveAsync(BloomFilterKeys.Pending(target), value);

            var stats = _registry.GetStats(target);
            Logger.Info(
                $"[BloomFilterRebuildRunner] Published '{target}' generation {nextGeneration} "
                + $"({sourceCount} values, {replayed.Length} replayed, "
                + $"{lateArrivals.Length} late, "
                + $"estimated false-positive rate {stats?.EstimatedFalsePositiveRate:P3}).");
        }
        finally
        {
            await _cache.ReleaseLockAsync(lockKey, lockToken);
        }
    }
}
