using backend.main.application.security;
using backend.main.application.security.authorization;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using static backend.tests.Unit.Application.Security.Authorization.MatrixEntry;

namespace backend.tests.Unit.Application.Security.Authorization;

/// <summary>
/// The CI permission gate (part of the Auth Contract Gate): every controller action and hub method
/// appears in <see cref="ApprovedPermissionMatrix"/>, and the edge checks the code declares are
/// exactly the approved ones.
/// </summary>
public class PermissionMatrixGateTests
{
    private static PermissionMatrixViolations EvaluateBackend() =>
        PermissionMatrixGate.Evaluate(
            EndpointAuthorizationAudit.Declare(EndpointAuthorizationAudit.BackendTypes()),
            ApprovedPermissionMatrix.Entries);

    [Fact]
    public void EveryEndpoint_ShouldBeClassifiedInTheApprovedMatrix()
    {
        EvaluateBackend().Unclassified.Should().BeEmpty(
            "a new endpoint needs an entry in ApprovedPermissionMatrix saying how it is authorized");
    }

    [Fact]
    public void ApprovedMatrix_ShouldNotListEndpointsThatNoLongerExist()
    {
        EvaluateBackend().Stale.Should().BeEmpty();
    }

    [Fact]
    public void DeclaredEdgeChecks_ShouldMatchTheApprovedMatrix()
    {
        EvaluateBackend().EdgeMismatches.Should().BeEmpty(
            "[RequirePermission] on an action or hub method must be the check the matrix approves");
    }

    [Fact]
    public void MatrixClasses_ShouldAgreeWithTheEndpointsAuthorizationMetadata()
    {
        EvaluateBackend().ClassMismatches.Should().BeEmpty();
    }

    [Fact]
    public void ApprovedMatrix_ShouldOnlyNameCatalogPermissions()
    {
        EvaluateBackend().UnknownPermissions.Should().BeEmpty();
    }

    [Fact]
    public void PublicEntries_ShouldBeTheReviewedPublicSurface()
    {
        var publicEntries = ApprovedPermissionMatrix.Entries
            .Where(pair => pair.Value.Class == MatrixClass.Public)
            .Select(pair => pair.Key);

        ReviewedPublicSurface.Endpoints.Should().Contain(publicEntries);
    }

    [Fact]
    public void EveryEndpointDeclaration_ShouldNameACatalogPermissionAndAParameterTheMethodHas()
    {
        var problems = new List<string>();
        foreach (var endpoint in EndpointAuthorizationAudit.Declare(EndpointAuthorizationAudit.BackendTypes()))
        {
            var parameters = endpoint.Method.GetParameters().Select(parameter => parameter.Name).ToHashSet();
            foreach (var declaration in endpoint.Method.GetCustomAttributes(typeof(RequirePermissionAttribute), true)
                .Cast<RequirePermissionAttribute>())
            {
                if (!Permissions.TryGet(declaration.PermissionName, out var permission))
                    problems.Add($"{endpoint.Key}: unknown permission {declaration.PermissionName}");
                else if (permission.Resource is ResourceKind.AuthoredContent or ResourceKind.ClubContent)
                    problems.Add($"{endpoint.Key}: {permission.Name} is checked in the service, not at the edge");
                if (!parameters.Contains(declaration.ResourceParameter))
                    problems.Add($"{endpoint.Key}: no parameter named {declaration.ResourceParameter}");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void EveryCatalogPermission_ShouldBeUsedByTheMatrix()
    {
        var used = ApprovedPermissionMatrix.Entries.Values.SelectMany(entry => entry.Permissions).ToHashSet();

        Permissions.All.Select(permission => permission.Name).Should().OnlyContain(
            name => used.Contains(name), "an unused permission is dead policy");
    }

    [Fact]
    public void Gate_ShouldReportAnEndpointMissingFromTheMatrix()
    {
        var violations = PermissionMatrixGate.Evaluate(
            EndpointAuthorizationAudit.Declare([typeof(DeclaringFixtureController)]),
            new Dictionary<string, MatrixEntry>());

        violations.Unclassified.Should().BeEquivalentTo(
            "PUT /api/fixtures/{clubId}", "GET /api/fixtures/admin", "GET /api/fixtures/public");
    }

    [Fact]
    public void Gate_ShouldReportAStaleEntry()
    {
        var violations = PermissionMatrixGate.Evaluate(
            [],
            new Dictionary<string, MatrixEntry> { ["GET /api/gone"] = Authenticated("gone") });

        violations.Stale.Should().ContainSingle().Which.Should().Be("GET /api/gone");
    }

    [Fact]
    public void Gate_ShouldReportADeclarationTheMatrixDoesNotApprove()
    {
        var violations = PermissionMatrixGate.Evaluate(
            EndpointAuthorizationAudit.Declare([typeof(DeclaringFixtureController)]),
            FixtureMatrix(Resource([], [PermissionNames.ClubManage])));

        violations.EdgeMismatches.Should().ContainSingle().Which.Should().Contain("declares [club.manage@clubId]");
    }

    [Fact]
    public void Gate_ShouldReportAnApprovedEdgeCheckTheCodeDoesNotDeclare()
    {
        var violations = PermissionMatrixGate.Evaluate(
            EndpointAuthorizationAudit.Declare([typeof(DeclaringFixtureController)]),
            FixtureMatrix(Resource(
                [Edge(PermissionNames.ClubManage, "clubId"), Edge(PermissionNames.ClubOwn, "clubId")], [])));

        violations.EdgeMismatches.Should().ContainSingle().Which.Should().Contain("club.own@clubId");
    }

    [Fact]
    public void Gate_ShouldAcceptAMatchingMatrix()
    {
        var violations = PermissionMatrixGate.Evaluate(
            EndpointAuthorizationAudit.Declare([typeof(DeclaringFixtureController)]),
            FixtureMatrix(Resource([Edge(PermissionNames.ClubManage, "clubId")], [PermissionNames.ClubManage])));

        violations.Any.Should().BeFalse();
    }

    [Fact]
    public void Gate_ShouldReportClassesThatContradictTheMetadata()
    {
        var violations = PermissionMatrixGate.Evaluate(
            EndpointAuthorizationAudit.Declare([typeof(DeclaringFixtureController)]),
            new Dictionary<string, MatrixEntry>
            {
                ["PUT /api/fixtures/{clubId}"] = Resource([Edge(PermissionNames.ClubManage, "clubId")], []),
                ["GET /api/fixtures/admin"] = Authenticated("not really"),
                ["GET /api/fixtures/public"] = Self("not really"),
            });

        violations.ClassMismatches.Should().HaveCount(2);
        violations.ClassMismatches.Should().Contain(problem => problem.Contains("AdminOnly"));
        violations.ClassMismatches.Should().Contain(problem => problem.Contains("not protected"));
    }

    private static Dictionary<string, MatrixEntry> FixtureMatrix(MatrixEntry resourceEntry) => new()
    {
        ["PUT /api/fixtures/{clubId}"] = resourceEntry,
        ["GET /api/fixtures/admin"] = AdminPolicy(),
        ["GET /api/fixtures/public"] = MatrixEntry.Public("fixture"),
    };

    [Route("fixtures")]
    public sealed class DeclaringFixtureController : ControllerBase
    {
        [Authorize]
        [HttpPut("{clubId}")]
        [RequirePermission(PermissionNames.ClubManage, "clubId")]
        public IActionResult Update(int clubId) => Ok(clubId);

        [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
        [HttpGet("admin")]
        public IActionResult Admin() => Ok();

        [AllowAnonymous]
        [HttpGet("public")]
        public IActionResult Open() => Ok();
    }
}
