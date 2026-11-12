using System.Text.Json;
using System.Text.RegularExpressions;

using backend.tests.Unit.Application.Security;

using FluentAssertions;

namespace backend.tests.Unit.Application.OpenApi;

/// <summary>
/// Pins the committed OpenAPI contract (<c>backend/openapi.json</c>) to the reviewed public
/// surface. CI regenerates the document and fails if it differs from the committed one, so
/// together these keep the published public/protected contract from drifting silently.
/// </summary>
public partial class OpenApiAuthorizationContractTests
{
    private static readonly string[] HttpMethods = ["get", "put", "post", "delete", "patch", "head", "options", "trace"];

    private static readonly Lazy<Operation[]> Operations = new(LoadOperations);

    [Fact]
    public void EveryOperation_ShouldDeclareItsAuthorization()
    {
        Operations.Value.Should().NotBeEmpty();
        Operations.Value
            .Where(operation => string.IsNullOrWhiteSpace(operation.Authorization))
            .Select(operation => operation.Key)
            .Should().BeEmpty("every operation carries x-authorization, so clients can tell public from protected");
    }

    [Fact]
    public void AnonymousOperations_ShouldMatchTheReviewedPublicSurface()
    {
        var anonymous = Operations.Value
            .Where(operation => operation.Authorization == "anonymous")
            .Select(operation => operation.Key);
        var reviewed = ReviewedPublicSurface.Endpoints
            .Where(key => !key.StartsWith(ReviewedPublicSurface.HubPrefix, StringComparison.Ordinal))
            .Select(Normalize);

        anonymous.Should().BeEquivalentTo(
            reviewed,
            "regenerate backend/openapi.json with export-openapi after changing the public surface");
    }

    [Fact]
    public void ProtectedOperations_ShouldRequireTheBearerScheme()
    {
        Operations.Value
            .Where(operation => operation.Authorization != "anonymous" && !operation.RequiresBearer)
            .Select(operation => operation.Key)
            .Should().BeEmpty("a protected operation without a security requirement tells clients it is public");
    }

    [Fact]
    public void AnonymousOperations_ShouldNotAdvertiseASecurityRequirement()
    {
        Operations.Value
            .Where(operation => operation.Authorization == "anonymous" && operation.RequiresBearer)
            .Select(operation => operation.Key)
            .Should().BeEmpty();
    }

    private static Operation[] LoadOperations()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "openapi.json")));
        var operations = new List<Operation>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject().Where(item => HttpMethods.Contains(item.Name)))
            {
                var authorization = method.Value.TryGetProperty("x-authorization", out var value)
                    ? value.GetString()
                    : null;
                var requiresBearer = method.Value.TryGetProperty("security", out var security)
                    && security.EnumerateArray().Any(requirement => requirement.TryGetProperty("bearerAuth", out _));

                operations.Add(new Operation(
                    Normalize($"{method.Name.ToUpperInvariant()} {path.Name}"),
                    authorization,
                    requiresBearer));
            }
        }

        return operations.ToArray();
    }

    // OpenAPI drops route constraints and optional markers; compare on the bare parameter names.
    private static string Normalize(string key) => RouteParameter().Replace(key, "{${name}}");

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)(:[^}]*)?\??\}")]
    private static partial Regex RouteParameter();

    private sealed record Operation(string Key, string? Authorization, bool RequiresBearer);
}
