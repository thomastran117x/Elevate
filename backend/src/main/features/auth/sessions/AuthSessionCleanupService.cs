using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.sessions;

/// <summary>
/// Runs <see cref="AuthSessionCleanupRunner"/> on a timer.
/// <para>
/// Six-hourly like the recently-viewed purge: rows are already a day past expiry when they become
/// eligible, and every read filters on the expiry itself, so nothing finer is needed. Not
/// leader-elected; the deletes are idempotent, so every replica sweeping is merely redundant.
/// </para>
/// </summary>
public sealed class AuthSessionCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    private readonly IServiceProvider _services;

    public AuthSessionCleanupService(IServiceProvider services)
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
                var runner = scope.ServiceProvider.GetRequiredService<AuthSessionCleanupRunner>();
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "[AuthSessionCleanupService] Failed to purge expired sessions.");
            }

            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
