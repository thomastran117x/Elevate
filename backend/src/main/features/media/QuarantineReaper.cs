using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Runs <see cref="QuarantineReaperRunner"/> hourly.
/// </summary>
/// <remarks>
/// Gated on the <c>storage</c> parent flag rather than <c>storage.quarantine</c>: switching
/// quarantine off must not strand the uploads it was already holding.
/// </remarks>
public sealed class QuarantineReaper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceProvider _services;

    public QuarantineReaper(IServiceProvider services)
    {
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<QuarantineReaperRunner>();
                var result = await runner.RunOnceAsync(stoppingToken);

                if (result.ExpiredAssets > 0 || result.DeletedBlobs > 0)
                {
                    Logger.Info(
                        $"[QuarantineReaper] Expired {result.ExpiredAssets} unattached uploads and deleted {result.DeletedBlobs} quarantined blobs.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[QuarantineReaper] Failed to reap the quarantine container.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
