using backend.main.application.security;
using backend.main.features.auth.events;

using backend.tests.Unit.Features.Auth.Events;

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
        AuthSecurityRequestFeature.For(context).EventRecorded = true;

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
    public void Emitter_ShouldMarkTheRequestOnceAnotherSecurityEventIsRecorded()
    {
        var context = new DefaultHttpContext();
        var emitter = AuthSecurityEventTestSupport.CreateEmitter(context, []);

        emitter.Emit(new AuthSecurityEvent
        {
            Definition = AuthSecurityEvents.SignInFailed,
            Outcome = AuthSecurityOutcome.Denied,
            Reason = AuthSecurityReason.AccountDisabled,
        });

        AuthSecurityRequestFeature.For(context).EventRecorded.Should().BeTrue();
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
        var responseFeature = new StartableResponseFeature();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);

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

    /// <summary>
    /// The default response feature never runs its OnStarting callbacks; this one runs them when
    /// the response starts, as a server does.
    /// </summary>
    private sealed class StartableResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];
        private bool _started;

        public override bool HasStarted => _started;

        public override void OnStarting(Func<object, Task> callback, object state) =>
            _onStarting.Add((callback, state));

        public async Task StartAsync()
        {
            if (_started)
                return;

            _started = true;
            for (var i = _onStarting.Count - 1; i >= 0; i--)
                await _onStarting[i].Callback(_onStarting[i].State);
        }
    }
}
