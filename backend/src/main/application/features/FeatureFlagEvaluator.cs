using Microsoft.Extensions.Options;

namespace backend.main.application.features;

public sealed class FeatureFlagEvaluator : IFeatureFlagEvaluator
{
    private readonly FeatureFlagRegistry _registry;
    private readonly IReadOnlyDictionary<string, bool> _configuredFlags;

    public FeatureFlagEvaluator(
        IOptions<FeatureFlagsOptions> options,
        FeatureFlagRegistry registry)
    {
        _registry = registry;
        _configuredFlags = new Dictionary<string, bool>(
            options.Value.Flags.ToDictionary(
                pair => registry.Normalize(pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in _configuredFlags.Keys)
            _registry.EnsureKnown(key);
    }

    public bool IsEnabled(string featureKey)
    {
        foreach (var key in _registry.GetLineage(featureKey))
        {
            if (_configuredFlags.TryGetValue(key, out var enabled) && !enabled)
                return false;
        }

        return true;
    }

    public IReadOnlyDictionary<string, bool> GetConfiguredFlags() => _configuredFlags;
}
