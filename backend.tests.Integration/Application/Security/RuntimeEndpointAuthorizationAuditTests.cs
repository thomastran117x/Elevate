using System.Reflection;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Application.Security;

/// <summary>
/// Negative fixtures for the runtime authorization gate. A throwaway host routes a minimal API
/// and a controller that omit authorization metadata, proving the same assertion the real app is
/// held to in <see cref="AuthorizationMatrixEndpointsTests"/> fails on them. No containers needed.
/// </summary>
public class RuntimeEndpointAuthorizationAuditTests
{
    [Fact]
    public async Task Gate_ShouldFail_ForMinimalApisAndControllersWithoutAuthorizationMetadata()
    {
        await using var app = BuildFixtureHost();

        var endpoints = RuntimeEndpointAuthorizationAudit.Audit(((IEndpointRouteBuilder)app).DataSources);
        var act = () => RuntimeEndpointAuthorizationAudit.AssertEveryEndpointIsClassified(endpoints);

        act.Should().Throw<UnclassifiedEndpointsException>()
            .Which.Endpoints.Should().BeEquivalentTo(
                "GET /fixtures/minimal-unclassified",
                "GET /fixtures/controller-unclassified");
    }

    [Fact]
    public async Task Audit_ShouldClassifyMinimalApisAndControllersByTheirMetadata()
    {
        await using var app = BuildFixtureHost();

        var endpoints = RuntimeEndpointAuthorizationAudit.Audit(((IEndpointRouteBuilder)app).DataSources);

        endpoints.Should().ContainEquivalentOf(
            new RuntimeEndpoint("GET", "/fixtures/minimal-public", RuntimeEndpointAccess.Anonymous));
        endpoints.Should().ContainEquivalentOf(
            new RuntimeEndpoint("POST", "/fixtures/minimal-protected", RuntimeEndpointAccess.Protected));
        endpoints.Should().ContainEquivalentOf(
            new RuntimeEndpoint("DELETE", "fixtures/controller-protected/{id:int}", RuntimeEndpointAccess.Protected));
    }

    [Fact]
    public void Gate_ShouldPass_WhenEveryEndpointIsClassified()
    {
        var act = () => RuntimeEndpointAuthorizationAudit.AssertEveryEndpointIsClassified(
        [
            new RuntimeEndpoint("GET", "api/a", RuntimeEndpointAccess.Anonymous),
            new RuntimeEndpoint("GET", "api/b", RuntimeEndpointAccess.Protected),
        ]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("api/events/{eventId:int}/manage", "/api/events/1/manage")]
    [InlineData("api/media/{publicId:guid}", "/api/media/00000000-0000-0000-0000-000000000001")]
    [InlineData("api/profile/{username}", "/api/profile/sample")]
    [InlineData("api/auth/providers/{provider}", "/api/auth/providers/sample")]
    [InlineData("api/files/{name}.{ext}", "/api/files/sample.sample")]
    [InlineData("", "/")]
    public void ProbePath_ShouldFillEachParameterWithAValueItsConstraintAccepts(string template, string expected)
    {
        RuntimeEndpointAuthorizationAudit.ProbePath(RoutePatternFactory.Parse(template)).Should().Be(expected);
    }

    private static WebApplication BuildFixtureHost()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(RuntimeEndpointAuthorizationAuditTests).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            // Route only the fixtures below, never anything else in the test assembly.
            manager.ApplicationParts.Clear();
            manager.FeatureProviders.Add(new FixtureControllerProvider());
        });

        var app = builder.Build();
        app.MapGet("/fixtures/minimal-unclassified", () => "fixture");
        app.MapGet("/fixtures/minimal-public", () => "fixture").AllowAnonymous();
        app.MapPost("/fixtures/minimal-protected", () => "fixture").RequireAuthorization();
        app.MapControllers();
        return app;
    }

    private sealed class FixtureControllerProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Add(typeof(UnclassifiedFixtureController).GetTypeInfo());
            feature.Controllers.Add(typeof(ProtectedFixtureController).GetTypeInfo());
        }
    }

    // Nested types are never discovered by MVC's default provider, so these stay out of the real app.
    [ApiController]
    [Route("fixtures/controller-unclassified")]
    public sealed class UnclassifiedFixtureController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [ApiController]
    [Authorize]
    [Route("fixtures/controller-protected")]
    public sealed class ProtectedFixtureController : ControllerBase
    {
        [HttpDelete("{id:int}")]
        public IActionResult Delete(int id) => Ok(id);
    }
}
