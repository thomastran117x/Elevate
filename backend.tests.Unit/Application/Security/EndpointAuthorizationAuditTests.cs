using System.Reflection;

using backend.main.features.auth;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;

namespace backend.tests.Unit.Application.Security;

/// <summary>
/// Requires every controller action and hub method to be explicitly public or protected, and pins
/// the public surface. The fallback policy already denies anonymous callers on anything left
/// unannotated; this audit keeps that from being the only thing standing between an endpoint
/// and the internet, and makes every new public endpoint a reviewed change to
/// <see cref="ExpectedAnonymousEndpoints"/>.
/// </summary>
public class EndpointAuthorizationAuditTests
{
    private static readonly Assembly BackendAssembly = typeof(AuthController).Assembly;

    // Keys are "VERB /api/route" for controller actions and "HUB Type.Method" for hub methods.
    private static readonly string[] ExpectedAnonymousEndpoints =
    [
        // Sign-in, sign-up, verification, OAuth, session refresh/logout, and account recovery.
        "GET /api/auth/csrf",
        "GET /api/auth/device/verify",
        "GET /api/auth/email/availability",
        "GET /api/auth/username/availability",
        "GET /api/auth/username/suggestions",
        "GET /api/auth/verify",
        "GET /api/auth/verify/email-change",
        "POST /api/auth/api/logout",
        "POST /api/auth/api/refresh",
        "POST /api/auth/change-password",
        "POST /api/auth/device/verify",
        "POST /api/auth/email/availability",
        "POST /api/auth/forgot-password",
        "POST /api/auth/google",
        "POST /api/auth/google/code",
        "POST /api/auth/login",
        "POST /api/auth/logout",
        "POST /api/auth/mfa/start",
        "POST /api/auth/mfa/verify",
        "POST /api/auth/mfa/verify/totp",
        "POST /api/auth/microsoft",
        "POST /api/auth/oauth/complete",
        "POST /api/auth/recovery/password",
        "POST /api/auth/recovery/username",
        "POST /api/auth/refresh",
        "POST /api/auth/reset-password",
        "POST /api/auth/signup",
        "POST /api/auth/verify",
        "POST /api/auth/verify/email-change",
        "POST /api/auth/verify/otp",

        // Club reads; private clubs are gated in the services.
        "GET /api/clubs",
        "GET /api/clubs/{clubId}/discussions",
        "GET /api/clubs/{clubId}/discussions/{discussionId}/replies",
        "GET /api/clubs/{clubId}/members",
        "GET /api/clubs/{clubId}/posts",
        "GET /api/clubs/{clubId}/posts/{id}",
        "GET /api/clubs/{clubId}/posts/{postId}/comments",
        "GET /api/clubs/{clubId}/reviews",
        "GET /api/clubs/{id}",
        "POST /api/clubs/search",

        // Event reads; private events are filtered by visibility in the services.
        "GET /api/events",
        "GET /api/events/batch",
        "GET /api/events/clubs/{clubId}",
        "GET /api/events/{eventId}",
        "GET /api/events/{eventId}/registrations",
        "POST /api/events/search",

        // Invitation resolution by token, so a recipient can preview before signing in.
        "POST /api/clubs/invitations/resolve",
        "POST /api/clubs/members/invitations/resolve",
        "POST /api/events/invitations/resolve",

        // Public profiles.
        "GET /api/profile/{username}",

        // Stripe webhook; authenticated by the Stripe-Signature header in the service.
        "POST /api/payments/webhook",

        // Club realtime hub; every join re-runs the REST read gate and typing requires a user.
        "HUB ClubRealtimeHub.JoinClub",
        "HUB ClubRealtimeHub.JoinDiscussion",
        "HUB ClubRealtimeHub.JoinPost",
        "HUB ClubRealtimeHub.LeaveClub",
        "HUB ClubRealtimeHub.LeaveDiscussion",
        "HUB ClubRealtimeHub.LeavePost",
        "HUB ClubRealtimeHub.RequestPresence",
        "HUB ClubRealtimeHub.Typing",
    ];

    [Fact]
    public void EveryEndpoint_ShouldBeExplicitlyPublicOrProtected()
    {
        var unclassified = EndpointAuthorizationAudit.Audit(BackendTypes())
            .Where(endpoint => endpoint.Access == EndpointAccess.Unclassified)
            .Select(endpoint => endpoint.Key);

        unclassified.Should().BeEmpty(
            "every endpoint needs [Authorize] (or a named policy) or [AllowAnonymous] on the action or its class");
    }

    [Fact]
    public void NoEndpoint_ShouldRequireAuthorizationUnderAClassLevelAllowAnonymous()
    {
        var conflicting = EndpointAuthorizationAudit.Audit(BackendTypes())
            .Where(endpoint => endpoint.Access == EndpointAccess.Conflicting)
            .Select(endpoint => endpoint.Key);

        conflicting.Should().BeEmpty(
            "[AllowAnonymous] on a class overrides every [Authorize] on its actions, so those actions are public");
    }

    [Fact]
    public void AnonymousEndpoints_ShouldMatchTheReviewedPublicSurface()
    {
        var anonymous = EndpointAuthorizationAudit.Audit(BackendTypes())
            .Where(endpoint => endpoint.Access == EndpointAccess.Anonymous)
            .Select(endpoint => endpoint.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        anonymous.Should().BeEquivalentTo(
            ExpectedAnonymousEndpoints,
            "a public endpoint is a deliberate decision; add or remove it from ExpectedAnonymousEndpoints in the same change");
    }

    [Fact]
    public void Audit_ShouldDiscoverTheApplicationControllersAndHubs()
    {
        var audited = EndpointAuthorizationAudit.Audit(BackendTypes());

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

    private static IEnumerable<Type> BackendTypes() =>
        BackendAssembly.GetTypes().Where(type =>
            ControllerDiscovery.IsDiscoverable(type) || typeof(Hub).IsAssignableFrom(type));

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

    // MVC only routes the types this provider accepts, so the scan uses the same rule.
    private sealed class ControllerDiscovery : ControllerFeatureProvider
    {
        private static readonly ControllerDiscovery Instance = new();

        public static bool IsDiscoverable(Type type) => Instance.IsController(type.GetTypeInfo());
    }
}

public enum EndpointAccess
{
    Unclassified,
    Anonymous,
    Protected,
    Conflicting,
}

public sealed record AuditedEndpoint(string Key, EndpointAccess Access);

/// <summary>
/// Classifies each action of the given MVC controllers and each method of the given SignalR hubs
/// by its authorization metadata, resolved the way ASP.NET Core combines class and action attributes.
/// </summary>
public static class EndpointAuthorizationAudit
{
    private const string ApiPrefix = "api";

    public static IReadOnlyList<AuditedEndpoint> Audit(IEnumerable<Type> types)
    {
        var candidates = types.ToArray();
        var endpoints = new List<AuditedEndpoint>();

        foreach (var controller in candidates.Where(IsController))
            endpoints.AddRange(AuditController(controller));

        foreach (var hub in candidates.Where(IsHub))
            endpoints.AddRange(AuditHub(hub));

        return endpoints;
    }

    private static IEnumerable<AuditedEndpoint> AuditController(Type controller)
    {
        var classTemplate = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
            .Select(route => route.Template)
            .FirstOrDefault() ?? string.Empty;

        foreach (var action in GetActions(controller))
        {
            var access = Classify(controller, action);
            var routes = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToArray();

            if (routes.Length == 0)
            {
                yield return new AuditedEndpoint($"ANY {CombineRoute(classTemplate, null)} ({action.Name})", access);
                continue;
            }

            foreach (var route in routes)
            {
                foreach (var verb in route.HttpMethods)
                    yield return new AuditedEndpoint($"{verb} {CombineRoute(classTemplate, route.Template)}", access);
            }
        }
    }

    private static IEnumerable<AuditedEndpoint> AuditHub(Type hub)
    {
        var methods = hub.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName
                && method.DeclaringType is not null
                && method.DeclaringType != typeof(object)
                && !method.DeclaringType.IsAssignableFrom(typeof(Hub))
                && method.GetBaseDefinition().DeclaringType != typeof(Hub));

        return methods.Select(method => new AuditedEndpoint($"HUB {hub.Name}.{method.Name}", Classify(hub, method)));
    }

    private static EndpointAccess Classify(Type type, MethodInfo method)
    {
        var classAnonymous = type.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();
        var classAuthorize = type.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any();
        var actionAnonymous = method.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();
        var actionAuthorize = method.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any();

        if (classAnonymous && actionAuthorize && !actionAnonymous)
            return EndpointAccess.Conflicting;
        if (classAnonymous || actionAnonymous)
            return EndpointAccess.Anonymous;
        if (classAuthorize || actionAuthorize)
            return EndpointAccess.Protected;
        return EndpointAccess.Unclassified;
    }

    private static bool IsController(Type type) =>
        !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type);

    private static bool IsHub(Type type) =>
        !type.IsAbstract && !type.ContainsGenericParameters && typeof(Hub).IsAssignableFrom(type);

    private static IEnumerable<MethodInfo> GetActions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName
                && method.DeclaringType is not null
                && method.DeclaringType != typeof(object)
                && !method.DeclaringType.IsAssignableFrom(typeof(Controller))
                && !method.IsDefined(typeof(NonActionAttribute), inherit: true)
                && method.Name != nameof(IDisposable.Dispose));

    private static string CombineRoute(string classTemplate, string? actionTemplate)
    {
        if (actionTemplate is not null
            && (actionTemplate.StartsWith('/') || actionTemplate.StartsWith("~/", StringComparison.Ordinal)))
        {
            return "/" + actionTemplate.TrimStart('~', '/');
        }

        var segments = new[] { ApiPrefix, classTemplate, actionTemplate }
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment!.Trim('/'));
        return "/" + string.Join('/', segments);
    }
}
