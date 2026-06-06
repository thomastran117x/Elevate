using System.Reflection;

using backend.main.application.bootstrap;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace backend.tests.Unit.Application.Bootstrap;

public class RoutesTests
{
    [Fact]
    public void RoutePaths_ShouldExposeExpectedApiConstants()
    {
        RoutePaths.ApiPrefix.Should().Be("api");
        RoutePaths.AuthPrefix.Should().Be("auth");
        RoutePaths.ApiAuthPath.Should().Be("/api/auth");
    }

    [Fact]
    public void RoutePrefixConvention_ShouldPrefixAttributeRoutes_AndSkipNullSelectors()
    {
        var convention = new RoutePrefixConvention(RoutePaths.ApiPrefix);
        var application = new ApplicationModel();
        var controller = new ControllerModel(typeof(TestController).GetTypeInfo(), []);
        var attributedSelector = new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel(new RouteAttribute("events"))
        };
        var nullSelector = new SelectorModel();
        controller.Selectors.Add(attributedSelector);
        controller.Selectors.Add(nullSelector);
        application.Controllers.Add(controller);

        convention.Apply(application);

        attributedSelector.AttributeRouteModel.Should().NotBeNull();
        attributedSelector.AttributeRouteModel!.Template.Should().Be("api/events");
        nullSelector.AttributeRouteModel.Should().BeNull();
    }

    private sealed class TestController;
}
