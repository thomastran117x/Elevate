using backend.main.application.security;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace backend.tests.Unit.Application.Security;

public class UnmatchedRouteAuthorizationResultHandlerTests
{
    private static readonly AuthorizationPolicy Policy =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    [Fact]
    public async Task HandleAsync_ShouldContinueThePipeline_WhenNoEndpointMatched()
    {
        var (context, authentication) = CreateContext(endpoint: null);
        var nextCalled = false;

        await new UnmatchedRouteAuthorizationResultHandler().HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            Policy,
            PolicyAuthorizationResult.Challenge());

        nextCalled.Should().BeTrue();
        authentication.Verify(
            service => service.ChallengeAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldContinueThePipeline_ForTheFrameworkMethodNotAllowedEndpoint()
    {
        var methodNotAllowed = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "405 HTTP Method Not Supported");
        var (context, _) = CreateContext(methodNotAllowed);
        var nextCalled = false;

        await new UnmatchedRouteAuthorizationResultHandler().HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            Policy,
            PolicyAuthorizationResult.Challenge());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldChallenge_WhenARoutedEndpointRejectsAnonymousCaller()
    {
        var (context, authentication) = CreateContext(CreateRouteEndpoint());
        var nextCalled = false;

        await new UnmatchedRouteAuthorizationResultHandler().HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            Policy,
            PolicyAuthorizationResult.Challenge());

        nextCalled.Should().BeFalse();
        authentication.Verify(
            service => service.ChallengeAsync(context, null, It.IsAny<AuthenticationProperties?>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldForbid_WhenARoutedEndpointRejectsAnAuthenticatedCaller()
    {
        var (context, authentication) = CreateContext(CreateRouteEndpoint());

        await new UnmatchedRouteAuthorizationResultHandler().HandleAsync(
            _ => Task.CompletedTask,
            context,
            Policy,
            PolicyAuthorizationResult.Forbid());

        authentication.Verify(
            service => service.ForbidAsync(context, null, It.IsAny<AuthenticationProperties?>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldContinueThePipeline_WhenARoutedEndpointIsAuthorized()
    {
        var (context, _) = CreateContext(CreateRouteEndpoint());
        var nextCalled = false;

        await new UnmatchedRouteAuthorizationResultHandler().HandleAsync(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            context,
            Policy,
            PolicyAuthorizationResult.Success());

        nextCalled.Should().BeTrue();
    }

    private static (DefaultHttpContext Context, Mock<IAuthenticationService> Authentication) CreateContext(Endpoint? endpoint)
    {
        var authentication = new Mock<IAuthenticationService>();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(authentication.Object)
                .BuildServiceProvider()
        };
        context.SetEndpoint(endpoint);
        return (context, authentication);
    }

    private static RouteEndpoint CreateRouteEndpoint() =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse("/api/auth/me"), 0, EndpointMetadataCollection.Empty, "GET /api/auth/me");
}
