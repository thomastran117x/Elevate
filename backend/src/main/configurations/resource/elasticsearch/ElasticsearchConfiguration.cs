using backend.main.configurations.environment;
using backend.main.utilities.implementation;

using Elastic.Clients.Elasticsearch;

namespace backend.main.configurations.resource.elasticsearch
{
    public static class ElasticsearchConfig
    {
        public static IServiceCollection AddAppElasticsearch(
            this IServiceCollection services,
            IConfiguration _)
        {
            var url = EnvironmentSetting.ElasticsearchUrl;

            if (string.IsNullOrWhiteSpace(url))
            {
                services.AddSingleton(new ElasticsearchHealth { IsAvailable = false });
                Logger.Warn("ELASTICSEARCH_URL not configured. Full-text search will fall back to MySQL LIKE.");
                return services;
            }

            try
            {
                var settings = new ElasticsearchClientSettings(new Uri(url));

                var client = new ElasticsearchClient(settings);
                services.AddSingleton(client);
                services.AddSingleton(new ElasticsearchHealth { IsAvailable = true });

                Logger.Info("Elasticsearch client registered.");
            }
            catch (Exception ex)
            {
                services.AddSingleton(new ElasticsearchHealth { IsAvailable = false, Failure = ex });
                Logger.Warn(ex, "Elasticsearch setup failed. Full-text search will fall back to MySQL LIKE.");
            }

            return services;
        }
    }
}
