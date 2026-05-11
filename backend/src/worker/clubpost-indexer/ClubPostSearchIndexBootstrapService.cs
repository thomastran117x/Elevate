using backend.main.infrastructure.elasticsearch;
using backend.main.services.interfaces;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace backend.worker.clubpost_indexer;

public sealed class ClubPostSearchIndexBootstrapService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ElasticsearchHealth _health;

    public ClubPostSearchIndexBootstrapService(
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
            Logger.Info("Club post index bootstrap skipped because Elasticsearch is disabled.");
            return;
        }

        using var scope = _services.CreateScope();

        try
        {
            var searchService = scope.ServiceProvider.GetRequiredService<IClubPostSearchService>();
            await searchService.EnsureIndexAsync(stoppingToken);
            Logger.Info("Club post Elasticsearch index verified during worker startup.");
        }
        catch (ElasticsearchDisabledException)
        {
            Logger.Info("Club post index bootstrap skipped because Elasticsearch is disabled.");
        }
        catch (ElasticsearchConfigurationException ex)
        {
            Logger.Error($"Club post index bootstrap failed due to configuration: {ex}");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Club post index bootstrap failed. The worker will retry lazily during indexing.");
        }
    }
}
