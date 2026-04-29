using Microsoft.Extensions.Hosting;

namespace backend.main.seeders
{
    public sealed class SeederOrchestrator : ISeederOrchestrator
    {
        private readonly IEnumerable<ISeeder> _seeders;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SeederOrchestrator> _logger;

        public SeederOrchestrator(
            IEnumerable<ISeeder> seeders,
            IHostEnvironment hostEnvironment,
            IConfiguration configuration,
            ILogger<SeederOrchestrator> logger
        )
        {
            _seeders = seeders;
            _hostEnvironment = hostEnvironment;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            if (!ShouldRunSeeders())
            {
                _logger.LogInformation(
                    "[Seeders] Skipping startup seeders. Seeders run automatically in development or when RUN_SEEDERS=true."
                );
                return;
            }

            var seederList = _seeders.ToList();

            if (seederList.Count == 0)
            {
                _logger.LogInformation("[Seeders] No startup seeders are registered.");
                return;
            }

            _logger.LogInformation(
                "[Seeders] Running {Count} startup seeder(s) in {Environment}.",
                seederList.Count,
                _hostEnvironment.EnvironmentName
            );

            foreach (var seeder in seederList)
                await seeder.SeedAsync(cancellationToken);
        }

        private bool ShouldRunSeeders()
        {
            if (_hostEnvironment.IsDevelopment())
                return true;

            return IsTruthy(_configuration["RUN_SEEDERS"])
                || IsTruthy(_configuration["SEEDERS_ENABLED"])
                || IsTruthy(_configuration["AUTH_SEED_USERS"]);
        }

        private static bool IsTruthy(string? value)
        {
            return bool.TryParse(value, out var enabled) && enabled;
        }
    }
}
