using backend.main.shared.utilities.logger;

namespace backend.main.features.media;

/// <summary>
/// Runs <see cref="MediaValidationReconcilerRunner"/> every minute while validation is handed to
/// media-worker.
/// </summary>
public sealed class MediaValidationReconciler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;

    public MediaValidationReconciler(IServiceProvider services)
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
                var runner = scope.ServiceProvider.GetRequiredService<MediaValidationReconcilerRunner>();
                var result = await runner.RunOnceAsync(stoppingToken);

                if (result.Redriven > 0 || result.Released > 0)
                {
                    Logger.Info(
                        $"[MediaValidationReconciler] Re-drove {result.Redriven} stalled media assets and released {result.Released} that can no longer be attached.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[MediaValidationReconciler] Failed to reconcile stalled media assets.");
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
