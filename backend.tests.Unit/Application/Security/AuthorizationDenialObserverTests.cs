using backend.main.application.security;
using backend.main.features.auth.events;

using backend.tests.Unit.Features.Auth.Events;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Application.Security;

public class AuthorizationDenialObserverTests
{
    [Fact]
    public void Observe_ShouldRecordAnUnexplained403WithTheRouteTemplate()
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var observer = CreateObserver(securityEvents);
        var context = CreateContext(StatusCodes.Status403Forbidden, "api/clubs/{clubId}");

        observer.Observe(context);

        var denied = securityEvents.Of(AuthSecurityEvents.AuthorizationDenied).Should().ContainSingle().Subject;
        denied.Outcome.Should().Be(AuthSecurityOutcome.Denied);
        denied.Reason.Should().Be(AuthSecurityReason.ResourceAccess);
        denied.Route.Should().Be("DELETE api/clubs/{clubId}");
        denied.UserId.Should().BeNull();
    }

    [Fact]
    public void Observe_ShouldAttributeTheDenialToTheSignedInCaller()
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var observer = CreateObserver(securityEvents);
        var context = CreateContext(StatusCodes.Status403Forbidden, "api/clubs/{clubId}");
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "42")],
            "Test"));

        observer.Observe(context);

        securityEvents.Of(AuthSecurityEvents.AuthorizationDenied).Should().ContainSingle()
            .Which.UserId.Should().Be(42);
    }

    [Theory]
    [InlineData(AuthSecurityReason.MfaRequired)]
    [InlineData(AuthSecurityReason.Policy)]
    public void Observe_ShouldUseTheReasonTheProducerMarked(AuthSecurityReason reason)
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var observer = CreateObserver(securityEvents);
        var context = CreateContext(StatusCodes.Status403Forbidden, "api/profile");
        AuthSecurityRequestFeature.MarkDenial(context, reason);

        observer.Observe(context);

        securityEvents.Of(AuthSecurityEvents.AuthorizationDenied).Should().ContainSingle()
            .Which.Reason.Should().Be(reason);
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status404NotFound)]
    public void Observe_ShouldIgnoreResponsesThatAreNotDenials(int statusCode)
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var observer = CreateObserver(securityEvents);

        observer.Observe(CreateContext(statusCode, "api/profile"));

        securityEvents.Emitted.Should().BeEmpty();
    }

    [Fact]
    public void Observe_ShouldNotCountA403AnotherEventAlreadyDescribes()
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var observer = CreateObserver(securityEvents);
        var context = CreateContext(StatusCodes.Status403Forbidden, "api/auth/login");
        AuthSecurityRequestFeature.For(context).DenialExplained = true;

        observer.Observe(context);

        securityEvents.Emitted.Should().BeEmpty();
    }

    [Fact]
    public void Observe_ShouldNeverUseTheRequestPath()
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var observer = CreateObserver(securityEvents);
        var context = new DefaultHttpContext();
        context.Request.Method = "PROPFIND";
        context.Request.Path = "/api/clubs/secret-value";
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.SetEndpoint(CreateEndpoint("api/clubs/{clubId}"));

        observer.Observe(context);

        securityEvents.Events.Should().ContainSingle().Which.Route.Should().Be("OTHER api/clubs/{clubId}");
    }

    [Fact]
    public void Emitter_ShouldMarkTheRequest_WhenAnEventExplainsTheForbiddenResponse()
    {
        var context = new DefaultHttpContext();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(context, []);

        emitter.Emit(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SignInFailed,
            Outcome = AuthSecurityOutcome.Denied,
            Reason = AuthSecurityReason.AccountDisabled,
        });

        AuthSecurityRequestFeature.For(context).DenialExplained.Should().BeTrue();
    }

    [Fact]
    public void Observe_ShouldStillRecordA403_AfterAnUnrelatedEvent()
    {
        // A session revoked earlier in the request (account deletion, a step-up refusal) says
        // nothing about a later ForbiddenException, so the denial is still recorded.
        var context = new DefaultHttpContext();
        var recording = new RecordingAuthSecurityEventSink();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(context, [recording]);
        var observer = new AuthorizationDenialObserverMiddleware(_ => Task.CompletedTask, emitter);
        emitter.Emit(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SessionRevoked,
            Outcome = AuthSecurityOutcome.Succeeded,
            Reason = AuthSecurityReason.AccountDeleted,
            UserId = 4,
        });
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.SetEndpoint(CreateEndpoint("api/clubs/{clubId}"));

        observer.Observe(context);

        recording.Events.Should().Contain(e => e.Definition == AuthSecurityEvents.AuthorizationDenied);
    }

    [Theory]
    [InlineData(AuthSecurityReason.AccountDisabled, AuthSecurityOutcome.Denied, true)]
    [InlineData(AuthSecurityReason.DeviceVerificationRequired, AuthSecurityOutcome.Succeeded, true)]
    [InlineData(AuthSecurityReason.AccountDisabled, AuthSecurityOutcome.Succeeded, false)]
    [InlineData(AuthSecurityReason.InvalidCredentials, AuthSecurityOutcome.Denied, false)]
    public void ExplainsForbidden_ShouldOnlyAcceptEventsThatAccountForA403(
        AuthSecurityReason reason,
        AuthSecurityOutcome outcome,
        bool expected)
    {
        AuthSecurityRequestFeature.ExplainsForbidden(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SignInFailed,
            Outcome = outcome,
            Reason = reason,
        }).Should().Be(expected);
    }

    [Fact]
    public async Task Middleware_ShouldObserveTheStatusCodeAsTheResponseStarts()
    {
        var securityEvents = new RecordingAuthSecurityEventEmitter();
        var services = new ServiceCollection()
            .AddSingleton<IAuthSecurityEventEmitter>(securityEvents)
            .BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseAuthorizationDenialObserver();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext { RequestServices = services };
        var responseFeature = CallbackResponseFeature.AttachTo(context);

        await app.Build()(context);
        securityEvents.Emitted.Should().BeEmpty("nothing is observed until the response starts");
        await responseFeature.StartAsync();

        securityEvents.Of(AuthSecurityEvents.AuthorizationDenied).Should().ContainSingle();
    }

    private static AuthorizationDenialObserverMiddleware CreateObserver(IAuthSecurityEventEmitter securityEvents) =>
        new(_ => Task.CompletedTask, securityEvents);

    private static DefaultHttpContext CreateContext(int statusCode, string routeTemplate)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "DELETE";
        context.Response.StatusCode = statusCode;
        context.SetEndpoint(CreateEndpoint(routeTemplate));
        return context;
    }

    private static RouteEndpoint CreateEndpoint(string routeTemplate) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(routeTemplate), 0, EndpointMetadataCollection.Empty, "test");
}
