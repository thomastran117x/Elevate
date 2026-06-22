using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.main.application.features;

public static class FeatureFlagServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureFlags(this IServiceCollection services)
    {
        var registry = FeatureFlagRegistry.Instance;

        services.AddSingleton(registry);

        // Lazy factory: IConfiguration is resolved from DI when first requested, which
        // happens after builder.Build(). At that point ConfigureAppConfiguration callbacks
        // (including test overrides from TestWebApplicationFactory) have already run.
        services.AddSingleton<IFeatureFlagEvaluator>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var opts = FeatureFlagsOptions.FromConfiguration(config, registry);
            return new FeatureFlagEvaluator(Options.Create(opts), registry);
        });

        // FeatureGateConvention is added via IConfigureOptions<MvcOptions> so it is
        // wired up when IOptions<MvcOptions> is first resolved (during route building,
        // after Build()), at which point the lazy IFeatureFlagEvaluator above reads the
        // final configuration including any test overrides.
        services.AddSingleton<IConfigureOptions<MvcOptions>>(sp =>
            new ConfigureOptions<MvcOptions>(options =>
                options.Conventions.Add(
                    new FeatureGateConvention(sp.GetRequiredService<IFeatureFlagEvaluator>()))));

        return services;
    }
}
