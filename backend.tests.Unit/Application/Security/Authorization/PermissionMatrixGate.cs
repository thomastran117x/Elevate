using System.Reflection;

using backend.main.application.security;
using backend.main.application.security.authorization;

using Microsoft.AspNetCore.Authorization;

namespace backend.tests.Unit.Application.Security.Authorization;

/// <summary>How an endpoint is authorized.</summary>
public enum MatrixClass
{
    /// <summary>Reachable anonymously; listed in <see cref="ReviewedPublicSurface"/>.</summary>
    Public,

    /// <summary>The <see cref="AuthorizationPolicies.AdminOnly"/> policy.</summary>
    AdminPolicy,

    /// <summary>One or more catalog permissions, at the edge, in the service, or both.</summary>
    Resource,

    /// <summary>Any signed-in user; the reason says why nothing narrower applies.</summary>
    Authenticated,

    /// <summary>Acts only on the caller's own data, identified by the access token.</summary>
    Self,
}

public sealed record EdgeCheck(string Permission, string Parameter)
{
    public override string ToString() => $"{Permission}@{Parameter}";
}

/// <summary>One endpoint's approved authorization.</summary>
public sealed record MatrixEntry(
    MatrixClass Class,
    IReadOnlyList<EdgeCheck> EdgeChecks,
    IReadOnlyList<string> ServicePermissions,
    string? Note)
{
    public static MatrixEntry Public(string note) => new(MatrixClass.Public, [], [], note);

    public static MatrixEntry AdminPolicy() => new(MatrixClass.AdminPolicy, [], [], null);

    public static MatrixEntry Authenticated(string note) => new(MatrixClass.Authenticated, [], [], note);

    public static MatrixEntry Self(string note) => new(MatrixClass.Self, [], [], note);

    public static EdgeCheck Edge(string permission, string parameter) => new(permission, parameter);

    /// <param name="edge">Checks the action or hub method declares with <see cref="RequirePermissionAttribute"/>.</param>
    /// <param name="service">Permissions the service checks (again, or only there).</param>
    /// <param name="note">Why, when the checks alone do not say.</param>
    public static MatrixEntry Resource(EdgeCheck[] edge, string[] service, string? note = null) =>
        new(MatrixClass.Resource, edge, service, note);

    /// <summary>Every permission the entry relies on, at the edge or in the service.</summary>
    public IEnumerable<string> Permissions => EdgeChecks.Select(check => check.Permission).Concat(ServicePermissions).Distinct();
}

/// <summary>Everything that makes the permission matrix gate fail, by kind.</summary>
public sealed record PermissionMatrixViolations(
    IReadOnlyList<string> Unclassified,
    IReadOnlyList<string> Stale,
    IReadOnlyList<string> EdgeMismatches,
    IReadOnlyList<string> ClassMismatches,
    IReadOnlyList<string> UnknownPermissions)
{
    public bool Any =>
        Unclassified.Count > 0 || Stale.Count > 0 || EdgeMismatches.Count > 0
        || ClassMismatches.Count > 0 || UnknownPermissions.Count > 0;
}

/// <summary>
/// Compares the endpoints the backend declares with the approved matrix: every endpoint is
/// classified, every edge check in the matrix is the one the code declares, and each class agrees
/// with the endpoint's authorization metadata.
/// </summary>
public static class PermissionMatrixGate
{
    public static PermissionMatrixViolations Evaluate(
        IEnumerable<DeclaredEndpoint> endpoints,
        IReadOnlyDictionary<string, MatrixEntry> matrix)
    {
        var declared = endpoints.ToArray();
        var keys = declared.Select(endpoint => endpoint.Key).ToHashSet(StringComparer.Ordinal);

        var unclassified = declared
            .Where(endpoint => !matrix.ContainsKey(endpoint.Key))
            .Select(endpoint => endpoint.Key);
        var stale = matrix.Keys.Where(key => !keys.Contains(key));

        var edgeMismatches = new List<string>();
        var classMismatches = new List<string>();
        foreach (var endpoint in declared)
        {
            if (!matrix.TryGetValue(endpoint.Key, out var entry))
                continue;

            var actual = endpoint.Method.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
                .Select(attribute => new EdgeCheck(attribute.PermissionName, attribute.ResourceParameter).ToString())
                .Order(StringComparer.Ordinal)
                .ToArray();
            var approved = entry.EdgeChecks.Select(check => check.ToString()).Order(StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(approved))
            {
                edgeMismatches.Add(
                    $"{endpoint.Key}: declares [{string.Join(", ", actual)}], matrix approves [{string.Join(", ", approved)}]");
            }

            var problem = ClassProblem(endpoint, entry);
            if (problem is not null)
                classMismatches.Add($"{endpoint.Key}: {problem}");
        }

        var unknown = matrix
            .SelectMany(pair => pair.Value.Permissions.Select(permission => (pair.Key, permission)))
            .Where(pair => !Permissions.TryGet(pair.permission, out _))
            .Select(pair => $"{pair.Key}: {pair.permission}");

        return new PermissionMatrixViolations(
            Sorted(unclassified), Sorted(stale), Sorted(edgeMismatches), Sorted(classMismatches), Sorted(unknown));
    }

    private static string? ClassProblem(DeclaredEndpoint endpoint, MatrixEntry entry)
    {
        var policies = endpoint.Type.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>()
            .Concat(endpoint.Method.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>())
            .Select(data => data.Policy)
            .Where(policy => policy is not null)
            .ToHashSet(StringComparer.Ordinal);
        var isAdminPolicy = policies.Contains(AuthorizationPolicies.AdminOnly);

        return entry.Class switch
        {
            MatrixClass.AdminPolicy when !isAdminPolicy => "approved as AdminPolicy but does not require AdminOnly",
            not MatrixClass.AdminPolicy when isAdminPolicy => "requires AdminOnly but the matrix does not say so",
            MatrixClass.Public when endpoint.Access != EndpointAccess.Anonymous => "approved as Public but is not anonymous",
            MatrixClass.Authenticated or MatrixClass.Self or MatrixClass.AdminPolicy
                when endpoint.Access != EndpointAccess.Protected => $"approved as {entry.Class} but is not protected",
            MatrixClass.Resource when entry.EdgeChecks.Count == 0 && entry.ServicePermissions.Count == 0 => "a Resource entry names no permission",
            not MatrixClass.Resource when entry.EdgeChecks.Count > 0 || entry.ServicePermissions.Count > 0 => "only Resource entries name permissions",
            _ => null,
        };
    }

    private static string[] Sorted(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();
}
