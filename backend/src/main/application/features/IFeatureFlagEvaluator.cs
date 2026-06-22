namespace backend.main.application.features;

public interface IFeatureFlagEvaluator
{
    bool IsEnabled(string featureKey);
    IReadOnlyDictionary<string, bool> GetConfiguredFlags();
}
