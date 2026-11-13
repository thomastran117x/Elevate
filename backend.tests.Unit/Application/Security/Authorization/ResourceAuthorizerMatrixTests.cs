using backend.main.application.security.authorization;
using backend.main.shared.exceptions.http;
using backend.tests.Unit.Support;

using FluentAssertions;

namespace backend.tests.Unit.Application.Security.Authorization;

/// <summary>
/// Evaluates every row of the approved matrix against the real evaluators over a seeded database,
/// and checks the error each refusal is reported with.
/// </summary>
public sealed class ResourceAuthorizerMatrixTests : IAsyncLifetime
{
    private AuthorizationTestWorld _world = null!;

    public static TheoryData<string, string, Caller, Expect> Matrix()
    {
        var data = new TheoryData<string, string, Caller, Expect>();
        foreach (var row in PermissionExpectations.Rows)
            data.Add(row.Permission, row.Resource, row.Caller, row.Expected);
        return data;
    }

    public async Task InitializeAsync() => _world = await AuthorizationTestWorld.CreateAsync();

    public async Task DisposeAsync() => await _world.DisposeAsync();

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Permission_ShouldAnswerEachCallerAsTheApprovedMatrixSays(
        string permissionName, string resourceLabel, Caller caller, Expect expected)
    {
        var permission = Permissions.Get(permissionName);
        var resource = PermissionExpectations.Resources[resourceLabel];
        var actor = AuthorizationTestWorld.Actor(caller);

        var decision = await _world.Authorizer.EvaluateAsync(actor, permission, resource);

        Classify(permission, resource, actor, decision).Should().Be(
            expected, "{0} on the {1} for {2}", permissionName, resourceLabel, caller);
    }

    [Fact]
    public void Matrix_ShouldCoverEveryPermissionForEveryCaller()
    {
        foreach (var permission in Permissions.All)
        {
            var callers = PermissionExpectations.Rows
                .Where(row => row.Permission == permission.Name)
                .Select(row => row.Caller)
                .ToHashSet();

            callers.Should().BeEquivalentTo(
                Enum.GetValues<Caller>(), "{0} needs an approved answer for every caller", permission.Name);
        }
    }

    [Fact]
    public void Matrix_ShouldCheckEveryPermissionAgainstAMissingResource()
    {
        var withoutMissingCase = Permissions.All
            .Where(permission => permission.Resource is not (ResourceKind.User or ResourceKind.AuthoredContent))
            .Where(permission => !PermissionExpectations.Rows.Any(row =>
                row.Permission == permission.Name && row.Expected == Expect.NotFound))
            .Select(permission => permission.Name);

        withoutMissingCase.Should().BeEmpty("a missing resource must be reported as missing, not as a refusal");
    }

    [Fact]
    public void Matrix_ShouldOnlyUseResourcesOfThePermissionsKind()
    {
        foreach (var row in PermissionExpectations.Rows)
        {
            PermissionExpectations.Resources[row.Resource].Kind.Should().Be(
                Permissions.Get(row.Permission).Resource, "{0} is checked on a {1}", row.Permission, row.Resource);
        }
    }

    [Fact]
    public void DefaultDecision_ShouldBeADenial()
    {
        default(PermissionDecision).IsAllowed.Should().BeFalse("an uninitialized decision must fail closed");
    }

    [Fact]
    public async Task Evaluate_ShouldRejectAResourceOfTheWrongKind()
    {
        var act = () => _world.Authorizer.EvaluateAsync(
            AuthorizationTestWorld.Actor(Caller.Owner), Permissions.ClubManage, new EventResource(1));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*club.manage*");
    }

    [Fact]
    public async Task Evaluate_ShouldReadStaffRolesFromTheDatabaseOnEveryCall()
    {
        var manager = AuthorizationTestWorld.Actor(Caller.Manager);
        var club = new ClubResource(AuthorizationTestWorld.PublicClubId);
        (await _world.Authorizer.IsAllowedAsync(manager, Permissions.ClubManage, club)).Should().BeTrue();

        var staff = _world.Db.ClubStaff.Single(entry =>
            entry.ClubId == AuthorizationTestWorld.PublicClubId
            && entry.UserId == AuthorizationTestWorld.UserId(Caller.Manager));
        _world.Db.ClubStaff.Remove(staff);
        await _world.Db.SaveChangesAsync();

        (await _world.Authorizer.IsAllowedAsync(manager, Permissions.ClubManage, club)).Should().BeFalse(
            "a removed manager is refused on the next check, with no cached role in between");
    }

    [Fact]
    public async Task Evaluate_ShouldFollowAClubThatBecomesPrivate()
    {
        var unrelated = AuthorizationTestWorld.Actor(Caller.Unrelated);
        var club = new ClubResource(AuthorizationTestWorld.PublicClubId);
        (await _world.Authorizer.IsAllowedAsync(unrelated, Permissions.ClubRead, club)).Should().BeTrue();

        var entity = await _world.Db.Clubs.FindAsync(AuthorizationTestWorld.PublicClubId);
        entity!.isPrivate = true;
        await _world.Db.SaveChangesAsync();

        (await _world.Authorizer.IsAllowedAsync(unrelated, Permissions.ClubRead, club)).Should().BeFalse();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData(" Admin ")]
    public async Task Evaluate_ShouldRecogniseTheAdminRoleHoweverItIsCased(string role)
    {
        var actor = ResourceActor.User(AuthorizationTestWorld.UserId(Caller.Unrelated), role);

        (await _world.Authorizer.IsAllowedAsync(
            actor, Permissions.ClubOwn, new ClubResource(AuthorizationTestWorld.PublicClubId))).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureAsync_ShouldUseCallSiteMessages()
    {
        var act = () => _world.Authorizer.EnsureAsync(
            AuthorizationTestWorld.Actor(Caller.Anonymous),
            Permissions.ClubRead,
            new ClubResource(AuthorizationTestWorld.PrivateClubId),
            new PermissionMessages(Forbidden: "members only", Anonymous: "sign in first"));

        (await act.Should().ThrowAsync<UnauthorizedException>()).WithMessage("sign in first");

        var forbidden = () => _world.Authorizer.EnsureAsync(
            AuthorizationTestWorld.Actor(Caller.Unrelated),
            Permissions.ClubRead,
            new ClubResource(AuthorizationTestWorld.PrivateClubId),
            new PermissionMessages(Forbidden: "members only", Anonymous: "sign in first"));

        (await forbidden.Should().ThrowAsync<ForbiddenException>()).WithMessage("members only");
    }

    [Fact]
    public async Task EnsureAsync_ShouldReportAMissingResourceWithItsNotFoundMessage()
    {
        var act = () => _world.Authorizer.EnsureAsync(
            AuthorizationTestWorld.Actor(Caller.Owner),
            Permissions.ClubManage,
            new ClubResource(AuthorizationTestWorld.MissingId));

        (await act.Should().ThrowAsync<ResourceNotFoundException>())
            .WithMessage($"Club {AuthorizationTestWorld.MissingId} not found");
    }

    private static Expect Classify(
        Permission permission, IAuthorizationResource resource, ResourceActor actor, PermissionDecision decision)
    {
        if (decision.Outcome == PermissionOutcome.ResourceNotFound)
            return Expect.NotFound;
        if (decision.IsAllowed)
            return Expect.Allow;

        return PermissionDenialErrors.For(permission, resource, actor, decision) switch
        {
            ResourceNotFoundException => Expect.Hide,
            UnauthorizedException => Expect.Challenge,
            ForbiddenException => Expect.Forbid,
            var other => throw new InvalidOperationException($"Unexpected refusal {other.GetType().Name}."),
        };
    }
}
