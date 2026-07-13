using backend.main.shared.utilities.logger;

namespace backend.main.shared.storage.cleanup
{
    public sealed class OrphanBlobCleanupService : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        private readonly IServiceProvider _services;

        public OrphanBlobCleanupService(IServiceProvider services)
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
                    var runner = scope.ServiceProvider.GetRequiredService<OrphanBlobCleanupRunner>();
                    await runner.RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "[OrphanBlobCleanupService] Failed to process orphaned blob cleanup.");
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
}
