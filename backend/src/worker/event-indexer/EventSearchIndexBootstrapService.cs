using backend.main.infrastructure.elasticsearch;
using backend.main.features.events.search;
using backend.main.shared.utilities.logger;

namespace backend.worker.event_indexer;

public sealed class EventSearchIndexBootstrapService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ElasticsearchHealth _health;

    public EventSearchIndexBootstrapService(
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
            Logger.Info("Event index bootstrap skipped because Elasticsearch is disabled.");
            return;
        }

        using var scope = _services.CreateScope();

        try
        {
            var searchService = scope.ServiceProvider.GetRequiredService<IEventSearchService>();
            await searchService.EnsureIndexAsync(stoppingToken);
            Logger.Info("Event Elasticsearch index verified during worker startup.");
        }
        catch (ElasticsearchDisabledException)
        {
            Logger.Info("Event index bootstrap skipped because Elasticsearch is disabled.");
        }
        catch (ElasticsearchConfigurationException ex)
        {
            Logger.Error($"Event index bootstrap failed due to configuration: {ex}");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Event index bootstrap failed. The worker will retry lazily during indexing.");
        }
    }
}
