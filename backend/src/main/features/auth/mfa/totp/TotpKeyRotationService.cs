using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.mfa.totp
{
    /// <summary>
    /// Runs <see cref="TotpKeyRotationRunner"/> at startup and then hourly, but only while
    /// <c>AUTH_TOTP_ENCRYPTION_KEYS_PREVIOUS</c> is set. Outside a rotation it exits at once.
    /// Not leader-elected: the runner's conditional writes make concurrent replicas redundant,
    /// never conflicting.
    /// </summary>
    public sealed class TotpKeyRotationService : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

        private readonly IServiceProvider _services;
        private readonly TotpSecretProtector _protector;

        public TotpKeyRotationService(IServiceProvider services, TotpSecretProtector protector)
        {
            _services = services;
            _protector = protector;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_protector.HasPreviousKeys)
                return;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<TotpKeyRotationRunner>();
                    await runner.RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "[TotpKeyRotationService] Failed to re-encrypt TOTP secrets under the active key.");
                }

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
