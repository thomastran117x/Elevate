using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace backend.main.application.features;

public sealed class FeatureGateConvention : IApplicationModelConvention
{
    private readonly IFeatureFlagEvaluator _featureFlags;

    public FeatureGateConvention(IFeatureFlagEvaluator featureFlags)
    {
        _featureFlags = featureFlags;
    }

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.ToList())
        {
            if (!AreEnabled(controller.Attributes))
            {
                application.Controllers.Remove(controller);
                continue;
            }

            foreach (var action in controller.Actions.ToList())
            {
                if (!AreEnabled(action.Attributes))
                    controller.Actions.Remove(action);
            }

            if (controller.Actions.Count == 0)
                application.Controllers.Remove(controller);
        }
    }

    private bool AreEnabled(IReadOnlyList<object> attributes)
    {
        foreach (var attribute in attributes.OfType<FeatureGateAttribute>())
        {
            if (!_featureFlags.IsEnabled(attribute.FeatureKey))
                return false;
        }

        return true;
    }
}
