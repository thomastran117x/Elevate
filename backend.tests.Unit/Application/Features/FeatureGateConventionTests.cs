using System.Reflection;

using backend.main.application.features;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Application.Features;

public class FeatureGateConventionTests
{
    [Fact]
    public void Apply_ShouldRemoveController_WhenControllerFeatureIsDisabled()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(CreateControllerModel<DisabledController>());

        var convention = new FeatureGateConvention(CreateEvaluator(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.Auth] = false
        }));

        convention.Apply(application);

        application.Controllers.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ShouldRemoveOnlyDisabledActions_WhenSiblingActionsRemainEnabled()
    {
        var controller = CreateControllerModel<MixedController>();
        var application = new ApplicationModel();
        application.Controllers.Add(controller);

        var convention = new FeatureGateConvention(CreateEvaluator(new Dictionary<string, bool>
        {
            [FeatureFlagKeys.EventsInvitations] = false
        }));

        convention.Apply(application);

        application.Controllers.Should().ContainSingle();
        controller.Actions.Select(action => action.ActionMethod.Name)
            .Should().Equal(nameof(MixedController.EnabledAction));
    }

    private static ControllerModel CreateControllerModel<TController>()
    {
        var typeInfo = typeof(TController).GetTypeInfo();
        var controller = new ControllerModel(typeInfo, typeInfo.GetCustomAttributes(inherit: true).ToList());

        foreach (var method in typeInfo.DeclaredMethods.Where(method => method.IsPublic && !method.IsSpecialName))
        {
            controller.Actions.Add(new ActionModel(method, method.GetCustomAttributes(inherit: true).ToList()));
        }

        return controller;
    }

    private static FeatureFlagEvaluator CreateEvaluator(IDictionary<string, bool> flags)
    {
        return new FeatureFlagEvaluator(
            Options.Create(new FeatureFlagsOptions
            {
                Flags = new Dictionary<string, bool>(flags)
            }),
            FeatureFlagRegistry.Instance);
    }

    [FeatureGate(FeatureFlagKeys.Auth)]
    private sealed class DisabledController
    {
        public void Index() { }
    }

    private sealed class MixedController
    {
        [FeatureGate(FeatureFlagKeys.EventsInvitations)]
        public void DisabledAction() { }

        public void EnabledAction() { }
    }
}
