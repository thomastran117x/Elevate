using System.Reflection;

using backend.main.features.auth;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;

namespace backend.tests.Unit.Application.Security;

public enum EndpointAccess
{
    Unclassified,
    Anonymous,
    Protected,
    Conflicting,
}

public sealed record AuditedEndpoint(string Key, EndpointAccess Access);

/// <summary>An audited endpoint together with the type and method that declare it.</summary>
public sealed record DeclaredEndpoint(string Key, EndpointAccess Access, Type Type, MethodInfo Method);

/// <summary>
/// Classifies each action of the given MVC controllers and each method of the given SignalR hubs
/// by its authorization metadata, resolved the way ASP.NET Core combines class and action attributes.
/// </summary>
public static class EndpointAuthorizationAudit
{
    private const string ApiPrefix = "api";

    /// <summary>The controllers MVC would route and the hubs the backend assembly declares.</summary>
    public static IEnumerable<Type> BackendTypes() =>
        typeof(AuthController).Assembly.GetTypes().Where(type =>
            ControllerDiscovery.IsDiscoverable(type) || typeof(Hub).IsAssignableFrom(type));

    public static IReadOnlyList<AuditedEndpoint> Audit(IEnumerable<Type> types) =>
        Declare(types).Select(endpoint => new AuditedEndpoint(endpoint.Key, endpoint.Access)).ToArray();

    /// <summary>Every endpoint with the method that implements it, keyed as <see cref="Audit"/> keys them.</summary>
    public static IReadOnlyList<DeclaredEndpoint> Declare(IEnumerable<Type> types)
    {
        var candidates = types.ToArray();
        var endpoints = new List<DeclaredEndpoint>();

        foreach (var controller in candidates.Where(IsController))
            endpoints.AddRange(AuditController(controller));

        foreach (var hub in candidates.Where(IsHub))
            endpoints.AddRange(AuditHub(hub));

        return endpoints;
    }

    private static IEnumerable<DeclaredEndpoint> AuditController(Type controller)
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
                yield return new DeclaredEndpoint($"ANY {CombineRoute(classTemplate, null)} ({action.Name})", access, controller, action);
                continue;
            }

            foreach (var route in routes)
            {
                foreach (var verb in route.HttpMethods)
                    yield return new DeclaredEndpoint($"{verb} {CombineRoute(classTemplate, route.Template)}", access, controller, action);
            }
        }
    }

    private static IEnumerable<DeclaredEndpoint> AuditHub(Type hub)
    {
        var methods = hub.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName
                && method.DeclaringType is not null
                && method.DeclaringType != typeof(object)
                && !method.DeclaringType.IsAssignableFrom(typeof(Hub))
                && method.GetBaseDefinition().DeclaringType != typeof(Hub));

        return methods.Select(method => new DeclaredEndpoint($"HUB {hub.Name}.{method.Name}", Classify(hub, method), hub, method));
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

    // MVC only routes the types this provider accepts, so the scan uses the same rule.
    private sealed class ControllerDiscovery : ControllerFeatureProvider
    {
        private static readonly ControllerDiscovery Instance = new();

        public static bool IsDiscoverable(Type type) => Instance.IsController(type.GetTypeInfo());
    }
}

/// <summary>Everything that would make the authorization gate fail, by kind.</summary>
public sealed record EndpointAuthorizationViolations(
    IReadOnlyList<string> Unclassified,
    IReadOnlyList<string> Conflicting,
    IReadOnlyList<string> UnreviewedPublic,
    IReadOnlyList<string> ReviewedButNotPublic)
{
    public bool Any =>
        Unclassified.Count > 0 || Conflicting.Count > 0 || UnreviewedPublic.Count > 0 || ReviewedButNotPublic.Count > 0;
}

/// <summary>
/// The CI authorization gate: every endpoint is explicitly public or protected, no class-level
/// <c>[AllowAnonymous]</c> silently overrides an action's <c>[Authorize]</c>, and the public
/// surface is exactly the reviewed list.
/// </summary>
public static class EndpointAuthorizationGate
{
    public static EndpointAuthorizationViolations Evaluate(
        IEnumerable<AuditedEndpoint> audited,
        IEnumerable<string> reviewedPublic)
    {
        var endpoints = audited.ToArray();
        var reviewed = reviewedPublic.ToHashSet(StringComparer.Ordinal);
        var anonymous = endpoints
            .Where(endpoint => endpoint.Access == EndpointAccess.Anonymous)
            .Select(endpoint => endpoint.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new EndpointAuthorizationViolations(
            Keys(endpoints, EndpointAccess.Unclassified),
            Keys(endpoints, EndpointAccess.Conflicting),
            anonymous.Where(key => !reviewed.Contains(key)).Order(StringComparer.Ordinal).ToArray(),
            reviewed.Where(key => !anonymous.Contains(key)).Order(StringComparer.Ordinal).ToArray());
    }

    private static string[] Keys(IEnumerable<AuditedEndpoint> endpoints, EndpointAccess access) =>
        endpoints
            .Where(endpoint => endpoint.Access == access)
            .Select(endpoint => endpoint.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
