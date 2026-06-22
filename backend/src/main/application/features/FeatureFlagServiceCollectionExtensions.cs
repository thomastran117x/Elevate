using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.main.application.features;

public static class FeatureFlagServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureFlags(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var registry = FeatureFlagRegistry.Instance;
        var options = FeatureFlagsOptions.FromConfiguration(configuration, registry);
        var optionsWrapper = Options.Create(options);
        var evaluator = new FeatureFlagEvaluator(optionsWrapper, registry);

        services.AddSingleton(registry);
        services.AddSingleton<IOptions<FeatureFlagsOptions>>(optionsWrapper);
        services.AddSingleton<IFeatureFlagEvaluator>(evaluator);

        return services;
    }
}
