using System.Reflection;
using System.Security.Claims;

using backend.main.application.features;
using backend.main.application.security.authorization;
using backend.main.shared.responses;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using static backend.tests.Unit.Support.AuthorizationTestWorld;

namespace backend.tests.Unit.Application.Security.Authorization;

public sealed class ResourcePermissionFilterTests : IAsyncLifetime
{
    private AuthorizationTestWorld _world = null!;

    public async Task InitializeAsync() => _world = await AuthorizationTestWorld.CreateAsync();

    public async Task DisposeAsync() => await _world.DisposeAsync();

    [Fact]
    public async Task ActionFilter_ShouldRunTheActionForAnAllowedCaller()
    {
        var (result, ran) = await RunActionAsync(
            Caller.Manager, new RequirePermissionAttribute(PermissionNames.ClubManage, "clubId"), PublicClubId);

        ran.Should().BeTrue();
        result.Should().BeNull();
    }

    [Fact]
    public async Task ActionFilter_ShouldAnswerARefusalWithTheForbiddenEnvelopeAndCallSiteMessage()
    {
        var (result, ran) = await RunActionAsync(
            Caller.Volunteer,
            new RequirePermissionAttribute(PermissionNames.ClubManage, "clubId") { DenialMessage = "Managers only." },
            PublicClubId);

        ran.Should().BeFalse();
        AssertEnvelope(result, StatusCodes.Status403Forbidden, "FORBIDDEN", "Managers only.");
    }

    [Fact]
    public async Task ActionFilter_ShouldChallengeAnAnonymousCallerOfAPrivateClub()
    {
        var (result, ran) = await RunActionAsync(
            Caller.Anonymous,
            new RequirePermissionAttribute(PermissionNames.ClubRead, "clubId") { AnonymousDenialMessage = "Sign in." },
            PrivateClubId);

        ran.Should().BeFalse();
        AssertEnvelope(result, StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Sign in.");
    }

    [Fact]
    public async Task ActionFilter_ShouldHideAnExistenceSensitiveResource()
    {
        var (result, ran) = await RunActionAsync(
            Caller.Unrelated, new RequirePermissionAttribute(PermissionNames.EventView, "eventId"), PrivateEventId);

        ran.Should().BeFalse();
        AssertEnvelope(result, StatusCodes.Status404NotFound, "RESOURCE_NOT_FOUND", $"Event {PrivateEventId} not found");
    }

    [Fact]
    public async Task ActionFilter_ShouldLeaveAMissingResourceToTheAction()
    {
        var (_, ran) = await RunActionAsync(
            Caller.Volunteer, new RequirePermissionAttribute(PermissionNames.ClubManage, "clubId"), MissingId);

        ran.Should().BeTrue("the action reports a missing resource with its usual message");
    }

    [Fact]
    public async Task ActionFilter_ShouldLeaveAnUnparsableIdToTheAction()
    {
        var (_, ran) = await RunActionAsync(
            Caller.Volunteer, new RequirePermissionAttribute(PermissionNames.ClubManage, "clubId"), "not-a-number");

        ran.Should().BeTrue();
    }

    [Fact]
    public async Task ActionFilter_ShouldFallBackToRouteValuesForUnboundParameters()
    {
        var (result, ran) = await RunActionAsync(
            Caller.Volunteer,
            new RequirePermissionAttribute(PermissionNames.ClubManage, "clubId"),
            PublicClubId,
            bindAsArgument: false);

        ran.Should().BeFalse();
        AssertEnvelope(result, StatusCodes.Status403Forbidden, "FORBIDDEN", "Not allowed");
    }

    [Fact]
    public async Task ActionFilter_ShouldCheckEveryDeclarationInOrder()
    {
        var (result, ran) = await RunActionAsync(
            Caller.Manager,
            [
                new RequirePermissionAttribute(PermissionNames.ClubManage, "clubId"),
                new RequirePermissionAttribute(PermissionNames.ClubOwn, "clubId") { DenialMessage = "Owner only." },
            ],
            PublicClubId);

        ran.Should().BeFalse();
        AssertEnvelope(result, StatusCodes.Status403Forbidden, "FORBIDDEN", "Owner only.");
    }

    [Fact]
    public void Declaration_ShouldRefuseAServiceOnlyPermissionOnAnEndpoint()
    {
        var declaration = new RequirePermissionAttribute(PermissionNames.ContentAuthor, "id");

        var act = () => declaration.CreateResource(1);

        act.Should().Throw<InvalidOperationException>().WithMessage("*checked in the service*");
    }

    [Fact]
    public void Declaration_ShouldParseMediaAssetIdsAsGuids()
    {
        new RequirePermissionAttribute(PermissionNames.MediaView, "publicId").CreateResource(ClubAssetId)
            .Should().Be(new MediaAssetResource(ClubAssetId));
        new RequirePermissionAttribute(PermissionNames.MediaView, "publicId").CreateResource("nope")
            .Should().BeNull();
    }

    [Fact]
    public async Task HubFilter_ShouldInvokeTheMethodForAnAllowedCaller()
    {
        var hub = await InvokeHubAsync(Caller.Member, nameof(FixtureHub.JoinClub), PrivateClubId);

        hub.Invoked.Should().BeTrue();
    }

    [Fact]
    public async Task HubFilter_ShouldRefuseWithTheRestMessage()
    {
        var act = () => InvokeHubAsync(Caller.Anonymous, nameof(FixtureHub.JoinClub), PrivateClubId);

        await act.Should().ThrowAsync<HubException>().WithMessage("Sign in to read.");

        var forbidden = () => InvokeHubAsync(Caller.Unrelated, nameof(FixtureHub.JoinClub), PrivateClubId);

        await forbidden.Should().ThrowAsync<HubException>().WithMessage("Members only.");
    }

    [Fact]
    public async Task HubFilter_ShouldLeaveAMissingClubToTheMethod()
    {
        var hub = await InvokeHubAsync(Caller.Unrelated, nameof(FixtureHub.JoinClub), MissingId);

        hub.Invoked.Should().BeTrue();
    }

    [Fact]
    public async Task HubFilter_ShouldCheckFeatureGatesBeforePermissions()
    {
        var act = () => InvokeHubAsync(
            Caller.Unrelated, nameof(FixtureHub.JoinClub), PrivateClubId, featureEnabled: false);

        await act.Should().ThrowAsync<HubException>().WithMessage("The 'fixture' feature is disabled.");
    }

    [Fact]
    public async Task HubFilter_ShouldRejectADeclarationOnAMissingParameter()
    {
        var act = () => InvokeHubAsync(Caller.Owner, nameof(FixtureHub.Misdeclared), PublicClubId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing parameter 'clubId'*");
    }

    private Task<(IActionResult? Result, bool Ran)> RunActionAsync(
        Caller caller, RequirePermissionAttribute declaration, object id, bool bindAsArgument = true) =>
        RunActionAsync(caller, [declaration], id, bindAsArgument);

    private async Task<(IActionResult? Result, bool Ran)> RunActionAsync(
        Caller caller, RequirePermissionAttribute[] declarations, object id, bool bindAsArgument = true)
    {
        var parameter = declarations[0].ResourceParameter;
        var httpContext = new DefaultHttpContext { User = Principal(caller) };
        var routeData = new RouteData();
        if (!bindAsArgument)
            routeData.Values[parameter] = id.ToString();

        var descriptor = new ActionDescriptor { EndpointMetadata = [.. declarations] };
        var actionContext = new ActionContext(httpContext, routeData, descriptor);
        var arguments = new Dictionary<string, object?>();
        if (bindAsArgument)
            arguments[parameter] = id;

        var executing = new ActionExecutingContext(actionContext, [], arguments, controller: new object());
        var ran = false;
        var filter = new ResourcePermissionActionFilter(_world.AuthorizationService);

        await filter.OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
        });

        return (executing.Result, ran);
    }

    private async Task<FixtureHub> InvokeHubAsync(Caller caller, string methodName, int clubId, bool featureEnabled = true)
    {
        var featureFlags = new Mock<IFeatureFlagEvaluator>();
        featureFlags.Setup(flags => flags.IsEnabled(It.IsAny<string>())).Returns(featureEnabled);

        var services = new ServiceCollection()
            .AddSingleton(featureFlags.Object)
            .AddSingleton(_world.AuthorizationService)
            .BuildServiceProvider();

        var hub = new FixtureHub();
        var callerContext = new Mock<HubCallerContext>();
        callerContext.SetupGet(context => context.User).Returns(Principal(caller));

        var method = typeof(FixtureHub).GetMethod(methodName)!;
        var invocation = new HubInvocationContext(callerContext.Object, services, hub, method, [clubId]);

        await new ResourcePermissionHubFilter().InvokeMethodAsync(invocation, context =>
        {
            ((FixtureHub)context.Hub).Invoked = true;
            return ValueTask.FromResult<object?>(null);
        });

        return hub;
    }

    private static ClaimsPrincipal Principal(Caller caller)
    {
        if (caller == Caller.Anonymous)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var actor = AuthorizationTestWorld.Actor(caller);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actor.UserId!.Value.ToString()),
            new Claim(ClaimTypes.Name, $"{caller}@test.local"),
            new Claim(ClaimTypes.Role, actor.Role!),
        ], "TestAuth"));
    }

    private static void AssertEnvelope(IActionResult? result, int status, string code, string message)
    {
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(status);
        var body = objectResult.Value.Should().BeOfType<ApiResponse<object?>>().Subject;
        body.Success.Should().BeFalse();
        body.Message.Should().Be(message);
        body.Error!.Code.Should().Be(code);
    }

    public sealed class FixtureHub : Hub
    {
        public bool Invoked { get; set; }

        [FeatureGate("fixture")]
        [RequirePermission(PermissionNames.ClubRead, "clubId",
            DenialMessage = "Members only.", AnonymousDenialMessage = "Sign in to read.")]
        public Task JoinClub(int clubId) => Task.CompletedTask;

        [RequirePermission(PermissionNames.ClubRead, "clubId")]
        public Task Misdeclared(int club) => Task.CompletedTask;
    }
}
