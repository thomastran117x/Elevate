using backend.main.application.environment;
using backend.main.utilities.implementation;

using Elastic.Clients.Elasticsearch;

namespace backend.main.infrastructure.elasticsearch
{
    public static class ElasticsearchConfig
    {
        public static IServiceCollection AddAppElasticsearch(
            this IServiceCollection services,
            IConfiguration _)
        {
            var url = EnvironmentSetting.ElasticsearchUrl;
            var health = new ElasticsearchHealth
            {
                IsConfigured = !string.IsNullOrWhiteSpace(url)
            };

            if (string.IsNullOrWhiteSpace(url))
            {
                services.AddSingleton(health);
                Logger.Warn("ELASTICSEARCH_URL not configured. Full-text search will fall back to MySQL LIKE.");
                return services;
            }

            try
            {
                var settings = new ElasticsearchClientSettings(new Uri(url));

                var client = new ElasticsearchClient(settings);
                services.AddSingleton(client);
                health.IsAvailable = true;
                services.AddSingleton(health);

                Logger.Info("Elasticsearch client registered.");
            }
            catch (Exception ex)
            {
                health.IsAvailable = false;
                health.Failure = ex;
                services.AddSingleton(health);
                Logger.Warn(ex, "Elasticsearch setup failed. Full-text search will fall back to MySQL LIKE.");
            }

            return services;
        }
    }
}
