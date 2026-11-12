using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace backend.tests.Integration.Application.Security;

public enum RuntimeEndpointAccess
{
    Unclassified,
    Anonymous,
    Protected,
}

/// <summary>One routable verb of one endpoint, as the running app resolved it.</summary>
public sealed record RuntimeEndpoint(string Method, string RouteTemplate, RuntimeEndpointAccess Access)
{
    public string Key => $"{Method} /{RouteTemplate.TrimStart('/')}";
}

/// <summary>
/// Classifies the endpoints a running app actually routes (controllers, hubs, and minimal APIs,
/// after feature gates) by the authorization metadata ASP.NET Core resolved for them. This is the
/// runtime half of the authorization gate; the reflection audit in the unit tests is the other.
/// </summary>
public static class RuntimeEndpointAuthorizationAudit
{
    // Endpoints with no HTTP method metadata accept any verb; GET is enough to probe them.
    private static readonly string[] AnyMethod = [HttpMethods.Get];

    public static IReadOnlyList<RuntimeEndpoint> Audit(IEnumerable<EndpointDataSource> sources) =>
        sources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .SelectMany(endpoint => Methods(endpoint).Select(method => new RuntimeEndpoint(
                method,
                endpoint.RoutePattern.RawText ?? string.Empty,
                Classify(endpoint))))
            .DistinctBy(endpoint => endpoint.Key)
            .OrderBy(endpoint => endpoint.Key, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<RuntimeEndpoint> Audit(EndpointDataSource source) => Audit([source]);

    /// <summary>Throws, naming every endpoint, when any routed endpoint has no classification.</summary>
    public static void AssertEveryEndpointIsClassified(IEnumerable<RuntimeEndpoint> endpoints)
    {
        var unclassified = endpoints
            .Where(endpoint => endpoint.Access == RuntimeEndpointAccess.Unclassified)
            .Select(endpoint => endpoint.Key)
            .ToArray();

        if (unclassified.Length > 0)
        {
            throw new UnclassifiedEndpointsException(unclassified);
        }
    }

    /// <summary>
    /// A request path that routes to the endpoint, with each parameter filled by a value its
    /// route constraint accepts. Only reachability is probed, so the values need not exist.
    /// </summary>
    public static string ProbePath(RoutePattern pattern)
    {
        var path = new StringBuilder();

        foreach (var segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (var part in segment.Parts)
            {
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => SampleValue(parameter),
                    _ => string.Empty,
                });
            }
        }

        return path.Length == 0 ? "/" : path.ToString();
    }

    private static RuntimeEndpointAccess Classify(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return RuntimeEndpointAccess.Anonymous;
        if (endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null)
            return RuntimeEndpointAccess.Protected;
        return RuntimeEndpointAccess.Unclassified;
    }

    private static IEnumerable<string> Methods(Endpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is { Count: > 0 } ? methods : AnyMethod;
    }

    private static string SampleValue(RoutePatternParameterPart parameter)
    {
        var constraints = parameter.ParameterPolicies
            .Select(policy => policy.Content?.Split('(')[0])
            .ToArray();

        if (constraints.Contains("guid"))
            return Guid.Parse("00000000-0000-0000-0000-000000000001").ToString();
        if (constraints.Contains("bool"))
            return "true";
        if (constraints.Contains("datetime"))
            return DateTime.UnixEpoch.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (constraints.Any(constraint => constraint is not null && NumericConstraints.Contains(constraint)))
            return "1";

        // Unconstrained parameters get a non-numeric value, so the probe cannot be captured by an
        // int-constrained sibling route that ranks ahead of it.
        return "sample";
    }

    private static readonly HashSet<string> NumericConstraints =
        new(["int", "long", "decimal", "double", "float", "min", "max", "range"], StringComparer.Ordinal);
}

public sealed class UnclassifiedEndpointsException(IReadOnlyList<string> endpoints)
    : Exception(
        "Every routed endpoint must declare [Authorize] (or a policy) or [AllowAnonymous]; "
        + "minimal APIs need RequireAuthorization() or AllowAnonymous(). Unclassified: "
        + string.Join(", ", endpoints))
{
    public IReadOnlyList<string> Endpoints { get; } = endpoints;
}
