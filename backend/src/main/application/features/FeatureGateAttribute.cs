namespace backend.main.application.features;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class FeatureGateAttribute : Attribute
{
    public FeatureGateAttribute(string featureKey)
    {
        FeatureKey = featureKey;
    }

    public string FeatureKey { get; }
}
