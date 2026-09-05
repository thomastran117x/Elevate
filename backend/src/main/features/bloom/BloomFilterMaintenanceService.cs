using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

namespace backend.main.features.bloom;

/// <summary>
/// Keeps the bloom filters alive: hydrates them at startup, merges shared bits on a short
/// interval, and rebuilds from the database on a long one.
/// </summary>
public sealed class BloomFilterMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly BloomFilterRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly BloomFilterOptions _options;

    public BloomFilterMaintenanceService(
        IServiceProvider services,
        BloomFilterRegistry registry,
        TimeProvider clock,
        IOptions<BloomFilterOptions> options)
    {
        _services = services;
        _registry = registry;
        _clock = clock;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshInterval = TimeSpan.FromSeconds(_options.RefreshIntervalSeconds);
        var rebuildInterval = TimeSpan.FromHours(_options.RebuildIntervalHours);
        var forcedRebuildCooldown = TimeSpan.FromMinutes(_options.ForcedRebuildCooldownMinutes);

        // Hydrate before serving lookups. Until this completes every lookup reports Unavailable
        // and callers query the database, so a slow start degrades throughput, never correctness.
        await RebuildAsync(stoppingToken);
        var lastRebuild = _clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(refreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var now = _clock.GetUtcNow();
            var elapsed = now - lastRebuild;

            // A failed write to the shared bitmap means the local and shared filters disagree,
            // and only a rebuild reconciles them. The cooldown stops a sustained Redis outage —
            // where every write fails — from scheduling a full table scan on every tick.
            //
            // The cooldown is checked first so the flag is consumed only when a rebuild can
            // actually follow. Reading it first would clear the divergence signal on a tick that
            // then declines to rebuild, losing it until the next six-hourly pass.
            var reconcileDivergence =
                elapsed >= forcedRebuildCooldown && _registry.ConsumeSharedStateDirty();

            if (elapsed >= rebuildInterval || reconcileDivergence)
            {
                await RebuildAsync(stoppingToken);
                lastRebuild = now;
                continue;
            }

            try
            {
                await _registry.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "[BloomFilterMaintenanceService] Refresh pass failed.");
            }
        }
    }

    private async Task RebuildAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<BloomFilterRebuildRunner>();
            await runner.RunOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; the loop checks the token and exits.
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "[BloomFilterMaintenanceService] Rebuild pass failed.");
        }
    }
}
