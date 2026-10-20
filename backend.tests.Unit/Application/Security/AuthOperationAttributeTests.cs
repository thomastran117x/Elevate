using backend.main.application.security;
using backend.main.features.auth.events;

using backend.tests.Unit.Features.Auth.Events;
using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

namespace backend.tests.Unit.Application.Security;

public class AuthOperationAttributeTests
{
    [Theory]
    [InlineData(StatusCodes.Status200OK, AuthSecurityOutcome.Succeeded)]
    [InlineData(StatusCodes.Status302Found, AuthSecurityOutcome.Succeeded)]
    [InlineData(StatusCodes.Status400BadRequest, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status401Unauthorized, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status409Conflict, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status429TooManyRequests, AuthSecurityOutcome.Throttled)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, AuthSecurityOutcome.Faulted)]
    public void Classify_ShouldSeparateRefusalsFromFaults(int statusCode, AuthSecurityOutcome expected)
    {
        AuthOperationAttribute.Classify(statusCode).Should().Be(expected);
    }

    [Theory]
    [InlineData(StatusCodes.Status401Unauthorized, "denied")]
    [InlineData(StatusCodes.Status429TooManyRequests, "throttled")]
    [InlineData(StatusCodes.Status500InternalServerError, "faulted")]
    public async Task Middleware_ShouldRecordTheStatusTheClientReceived(int statusCode, string outcome)
    {
        using var recorder = CreateRecorder(out var middleware, context =>
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });
        var (context, response) = CreateContext(AuthOperation.SignIn);

        await middleware.InvokeAsync(context);
        recorder.Measurements.Should().BeEmpty("nothing is recorded until the response completes");
        await response.CompleteAsync();

        var measurement = recorder.Measurements.Should().ContainSingle().Subject;
        measurement.Instrument.Should().Be("auth.operation.duration");
        measurement.Tags["operation"].Should().Be("sign_in");
        measurement.Tags["outcome"].Should().Be(outcome);
    }

    [Fact]
    public async Task Middleware_ShouldClassifyAnEscapedException_ByTheStatusTheOuterHandlerSends()
    {
        // A CSRF failure throws AntiforgeryValidationException, which is not an AppException; the
        // global exception handler, outside this middleware, answers it with 400.
        using var recorder = CreateRecorder(
            out var middleware,
            _ => throw new Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException("bad token"));
        var (context, response) = CreateContext(AuthOperation.SignIn);

        var act = () => middleware.InvokeAsync(context);
        await act.Should().ThrowAsync<Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException>();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await response.CompleteAsync();

        recorder.Measurements.Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("denied");
    }

    [Fact]
    public async Task Middleware_ShouldNotRecordARequestTheClientAbandoned()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        using var recorder = CreateRecorder(out var middleware, _ => Task.CompletedTask);
        var (context, response) = CreateContext(AuthOperation.SignIn);
        context.RequestAborted = aborted.Token;

        await middleware.InvokeAsync(context);
        await response.CompleteAsync();

        recorder.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_ShouldIgnoreEndpointsWithoutAnOperation()
    {
        using var recorder = CreateRecorder(out var middleware, _ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        var response = CallbackResponseFeature.AttachTo(context);
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test"));

        await middleware.InvokeAsync(context);
        await response.CompleteAsync();

        recorder.Measurements.Should().BeEmpty();
    }

    private static AuthMetricsRecorder CreateRecorder(
        out AuthOperationMetricsMiddleware middleware,
        RequestDelegate next)
    {
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        var recorder = new AuthMetricsRecorder(meterFactory);
        middleware = new AuthOperationMetricsMiddleware(next, new AuthSecurityMetrics(meterFactory), TimeProvider.System);
        return recorder;
    }

    private static (DefaultHttpContext Context, CallbackResponseFeature Response) CreateContext(AuthOperation operation)
    {
        var context = new DefaultHttpContext();
        var response = CallbackResponseFeature.AttachTo(context);
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthOperationAttribute(operation)),
            "test"));
        return (context, response);
    }
}
