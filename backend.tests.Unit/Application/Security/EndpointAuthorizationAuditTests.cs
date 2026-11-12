using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace backend.tests.Unit.Application.Security;

/// <summary>
/// Requires every controller action and hub method to be explicitly public or protected, and pins
/// the public surface. The fallback policy already denies anonymous callers on anything left
/// unannotated; this audit keeps that from being the only thing standing between an endpoint
/// and the internet, and makes every new public endpoint a reviewed change to
/// <see cref="ReviewedPublicSurface"/>. CI runs it as the Auth Contract Gate.
/// </summary>
public class EndpointAuthorizationAuditTests
{
    private static EndpointAuthorizationViolations EvaluateBackend() =>
        EndpointAuthorizationGate.Evaluate(
            EndpointAuthorizationAudit.Audit(EndpointAuthorizationAudit.BackendTypes()),
            ReviewedPublicSurface.Endpoints);

    [Fact]
    public void EveryEndpoint_ShouldBeExplicitlyPublicOrProtected()
    {
        EvaluateBackend().Unclassified.Should().BeEmpty(
            "every endpoint needs [Authorize] (or a named policy) or [AllowAnonymous] on the action or its class");
    }

    [Fact]
    public void NoEndpoint_ShouldRequireAuthorizationUnderAClassLevelAllowAnonymous()
    {
        EvaluateBackend().Conflicting.Should().BeEmpty(
            "[AllowAnonymous] on a class overrides every [Authorize] on its actions, so those actions are public");
    }

    [Fact]
    public void AnonymousEndpoints_ShouldMatchTheReviewedPublicSurface()
    {
        var violations = EvaluateBackend();

        violations.UnreviewedPublic.Should().BeEmpty(
            "a public endpoint is a deliberate decision; add it to ReviewedPublicSurface in the same change");
        violations.ReviewedButNotPublic.Should().BeEmpty(
            "an endpoint that is no longer public, or no longer exists, must leave ReviewedPublicSurface");
    }

    [Fact]
    public void ReviewedPublicSurface_ShouldNotListAnEndpointTwice()
    {
        ReviewedPublicSurface.Endpoints.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Audit_ShouldDiscoverTheApplicationControllersAndHubs()
    {
        var audited = EndpointAuthorizationAudit.Audit(EndpointAuthorizationAudit.BackendTypes());

        audited.Should().Contain(endpoint => endpoint.Key == "POST /api/auth/login");
        audited.Should().Contain(endpoint => endpoint.Key == "GET /api/auth/me"
            && endpoint.Access == EndpointAccess.Protected);
        audited.Should().Contain(endpoint => endpoint.Key.StartsWith("HUB ClubRealtimeHub.", StringComparison.Ordinal));
    }

    [Fact]
    public void Audit_ShouldReportAnActionWithNoAuthorizationAttribute()
    {
        var audited = EndpointAuthorizationAudit.Audit([typeof(UnclassifiedFixtureController)]);

        audited.Should().ContainSingle()
            .Which.Should().Be(new AuditedEndpoint("GET /api/fixtures/unclassified", EndpointAccess.Unclassified));
    }

    [Fact]
    public void Audit_ShouldClassifyClassAndActionAttributes()
    {
        var audited = EndpointAuthorizationAudit.Audit([typeof(ClassifiedFixtureController)]);

        audited.Should().BeEquivalentTo(
        [
            new AuditedEndpoint("GET /api/fixtures/classified", EndpointAccess.Protected),
            new AuditedEndpoint("GET /api/fixtures/classified/public", EndpointAccess.Anonymous),
            new AuditedEndpoint("POST /api/fixtures/classified/admin", EndpointAccess.Protected),
            new AuditedEndpoint("PUT /api/fixtures/classified/admin", EndpointAccess.Protected),
        ]);
    }

    [Fact]
    public void Audit_ShouldReportAnAuthorizeActionUnderAClassLevelAllowAnonymous()
    {
        var audited = EndpointAuthorizationAudit.Audit([typeof(ConflictingFixtureController)]);

        audited.Should().BeEquivalentTo(
        [
            new AuditedEndpoint("GET /api/fixtures/conflicting", EndpointAccess.Anonymous),
            new AuditedEndpoint("DELETE /api/fixtures/conflicting", EndpointAccess.Conflicting),
        ]);
    }

    [Fact]
    public void Audit_ShouldClassifyHubMethodsByTheirHubAndMethodAttributes()
    {
        EndpointAuthorizationAudit.Audit([typeof(UnclassifiedFixtureHub)])
            .Should().BeEquivalentTo(
            [
                new AuditedEndpoint("HUB UnclassifiedFixtureHub.Send", EndpointAccess.Unclassified),
                new AuditedEndpoint("HUB UnclassifiedFixtureHub.Join", EndpointAccess.Protected),
            ]);

        EndpointAuthorizationAudit.Audit([typeof(AnonymousFixtureHub)])
            .Should().ContainSingle()
            .Which.Should().Be(new AuditedEndpoint("HUB AnonymousFixtureHub.Send", EndpointAccess.Anonymous));
    }

    // Negative fixtures: each proves the gate the backend tests above rely on actually fails
    // when authorization metadata is omitted or the public surface drifts.

    [Fact]
    public void Gate_ShouldFail_WhenAnEndpointOmitsAuthorizationMetadata()
    {
        var violations = EndpointAuthorizationGate.Evaluate(
            EndpointAuthorizationAudit.Audit([typeof(UnclassifiedFixtureController), typeof(UnclassifiedFixtureHub)]),
            reviewedPublic: []);

        violations.Any.Should().BeTrue();
        violations.Unclassified.Should().Equal("GET /api/fixtures/unclassified", "HUB UnclassifiedFixtureHub.Send");
    }

    [Fact]
    public void Gate_ShouldFail_WhenAClassLevelAllowAnonymousOverridesAnAction()
    {
        var violations = EndpointAuthorizationGate.Evaluate(
            EndpointAuthorizationAudit.Audit([typeof(ConflictingFixtureController)]),
            reviewedPublic: ["GET /api/fixtures/conflicting"]);

        violations.Any.Should().BeTrue();
        violations.Conflicting.Should().Equal("DELETE /api/fixtures/conflicting");
        violations.UnreviewedPublic.Should().BeEmpty();
    }

    [Fact]
    public void Gate_ShouldFail_WhenAPublicEndpointIsNotReviewed()
    {
        var violations = EndpointAuthorizationGate.Evaluate(
            EndpointAuthorizationAudit.Audit([typeof(ClassifiedFixtureController), typeof(AnonymousFixtureHub)]),
            reviewedPublic: []);

        violations.Any.Should().BeTrue();
        violations.UnreviewedPublic.Should().Equal("GET /api/fixtures/classified/public", "HUB AnonymousFixtureHub.Send");
    }

    [Fact]
    public void Gate_ShouldFail_WhenAReviewedEndpointIsNoLongerPublic()
    {
        var violations = EndpointAuthorizationGate.Evaluate(
            EndpointAuthorizationAudit.Audit([typeof(ClassifiedFixtureController)]),
            reviewedPublic: ["GET /api/fixtures/classified/public", "GET /api/fixtures/classified"]);

        violations.Any.Should().BeTrue();
        violations.ReviewedButNotPublic.Should().Equal("GET /api/fixtures/classified");
    }

    [Fact]
    public void Gate_ShouldPass_WhenEveryEndpointIsClassifiedAndThePublicSurfaceIsReviewed()
    {
        var violations = EndpointAuthorizationGate.Evaluate(
            EndpointAuthorizationAudit.Audit([typeof(ClassifiedFixtureController)]),
            reviewedPublic: ["GET /api/fixtures/classified/public"]);

        violations.Any.Should().BeFalse();
    }

    // Nested public types are not discovered by MVC (it requires top-level public types), so
    // these fixtures cannot leak into any test host.
    [ApiController]
    [Route("fixtures/unclassified")]
    public sealed class UnclassifiedFixtureController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();
    }

    [ApiController]
    [Authorize]
    [Route("fixtures/classified")]
    public sealed class ClassifiedFixtureController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();

        [HttpGet("public")]
        [AllowAnonymous]
        public IActionResult GetPublic() => Ok();

        [HttpPost("admin")]
        [HttpPut("admin")]
        [Authorize("FixtureAdmin")]
        public IActionResult Admin() => Ok();

        [NonAction]
        public void Helper()
        {
        }
    }

    [ApiController]
    [AllowAnonymous]
    [Route("fixtures/conflicting")]
    public sealed class ConflictingFixtureController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok();

        [HttpDelete]
        [Authorize]
        public IActionResult Delete() => Ok();
    }

    public sealed class UnclassifiedFixtureHub : Hub
    {
        public Task Send() => Task.CompletedTask;

        [Authorize]
        public Task Join() => Task.CompletedTask;
    }

    [AllowAnonymous]
    public sealed class AnonymousFixtureHub : Hub
    {
        public Task Send() => Task.CompletedTask;

        public override Task OnConnectedAsync() => base.OnConnectedAsync();
    }
}
