using backend.main.features.clubs.posts.search;
using backend.main.services.interfaces;
using backend.main.shared.utilities.logger;

namespace backend.main.infrastructure.elasticsearch
{
    public sealed class ElasticsearchIndexInitializationService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ElasticsearchHealth _health;

        public ElasticsearchIndexInitializationService(
            IServiceProvider services,
            ElasticsearchHealth health)
        {
            _services = services;
            _health = health;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            if (!_health.IsConfigured)
            {
                Logger.Info("Elasticsearch index bootstrap skipped because Elasticsearch is disabled.");
                return;
            }

            using var scope = _services.CreateScope();

            try
            {
                var eventSearch = scope.ServiceProvider.GetRequiredService<IEventSearchService>();
                var clubPostSearch = scope.ServiceProvider.GetRequiredService<IClubPostSearchService>();

                await eventSearch.EnsureIndexAsync(stoppingToken);
                await clubPostSearch.EnsureIndexAsync(stoppingToken);

                Logger.Info("Elasticsearch indices verified during startup.");
            }
            catch (ElasticsearchDisabledException)
            {
                Logger.Info("Elasticsearch index bootstrap skipped because Elasticsearch is disabled.");
            }
            catch (ElasticsearchConfigurationException ex)
            {
                Logger.Error($"Elasticsearch index bootstrap failed due to configuration: {ex}");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Elasticsearch index bootstrap failed. Indices will be retried lazily on use.");
            }
        }
    }
}
