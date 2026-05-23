using System.Text.Json;

using backend.main.application.handlers;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Application.Handlers;

public class ApiResponseHandlerTests
{
    [Fact]
    public void AddApiResponseConventions_ShouldReturnStructuredValidationFailures()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        services.AddApiResponseConventions();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value;

        var modelState = new ModelStateDictionary();
        modelState.AddModelError("email", "Email is required.");
        modelState.AddModelError("password", string.Empty);

        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor(),
            modelState);

        var result = options.InvalidModelStateResponseFactory(actionContext);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(badRequest.Value));
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("message").GetString().Should().Be("Validation failed.");
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        json.RootElement.GetProperty("error").GetProperty("details").GetProperty("email")[0].GetString()
            .Should().Be("Email is required.");
        json.RootElement.GetProperty("error").GetProperty("details").GetProperty("password")[0].GetString()
            .Should().Be("The input was invalid.");
    }
}
