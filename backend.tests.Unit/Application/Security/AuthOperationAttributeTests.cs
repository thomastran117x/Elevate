using backend.main.application.security;
using backend.main.features.auth.events;
using backend.main.shared.exceptions.http;

using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

namespace backend.tests.Unit.Application.Security;

public class AuthOperationAttributeTests
{
    [Theory]
    [InlineData(StatusCodes.Status200OK, AuthSecurityOutcome.Succeeded)]
    [InlineData(StatusCodes.Status302Found, AuthSecurityOutcome.Succeeded)]
    [InlineData(StatusCodes.Status401Unauthorized, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status409Conflict, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status429TooManyRequests, AuthSecurityOutcome.Throttled)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, AuthSecurityOutcome.Faulted)]
    public void Classify_ShouldSeparateRefusalsFromFaults(int statusCode, AuthSecurityOutcome expected)
    {
        AuthOperationAttribute.Classify(statusCode).Should().Be(expected);
    }

    [Fact]
    public async Task Middleware_ShouldRecordTheOperationAndItsOutcome()
    {
        var (recorder, middleware) = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });
        using var _ = recorder;

        await middleware.InvokeAsync(CreateContext(AuthOperation.SignIn));

        var measurement = recorder.Measurements.Should().ContainSingle().Subject;
        measurement.Instrument.Should().Be("auth.operation.duration");
        measurement.Tags["operation"].Should().Be("sign_in");
        measurement.Tags["outcome"].Should().Be("denied");
    }

    [Fact]
    public async Task Middleware_ShouldRecordARateLimiterRejectionAsThrottled()
    {
        // The limiter short-circuits before MVC; the middleware runs ahead of it and still sees it.
        var (recorder, middleware) = CreateMiddleware(context =>
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Task.CompletedTask;
        });
        using var _ = recorder;

        await middleware.InvokeAsync(CreateContext(AuthOperation.SignIn));

        recorder.Measurements.Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("throttled");
    }

    [Fact]
    public async Task Middleware_ShouldRecordAFault_WhenAnUnexpectedExceptionEscapes()
    {
        var (recorder, middleware) = CreateMiddleware(_ => throw new InvalidOperationException("boom"));
        using var _ = recorder;

        var act = () => middleware.InvokeAsync(CreateContext(AuthOperation.Refresh));

        await act.Should().ThrowAsync<InvalidOperationException>();
        recorder.Measurements.Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("faulted");
    }

    [Fact]
    public async Task Middleware_ShouldClassifyAnEscapingAppExceptionByItsStatus()
    {
        var (recorder, middleware) = CreateMiddleware(_ => throw new ForbiddenException());
        using var _ = recorder;

        var act = () => middleware.InvokeAsync(CreateContext(AuthOperation.FactorChange));

        await act.Should().ThrowAsync<ForbiddenException>();
        recorder.Measurements.Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("denied");
    }

    [Fact]
    public async Task Middleware_ShouldNotRecordARequestTheClientAbandoned()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var (recorder, middleware) = CreateMiddleware(_ => throw new OperationCanceledException(aborted.Token));
        using var _ = recorder;
        var context = CreateContext(AuthOperation.SignIn);
        context.RequestAborted = aborted.Token;

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<OperationCanceledException>();
        recorder.Measurements.Should().BeEmpty();
    }

    [Fact]
    public async Task Middleware_ShouldIgnoreEndpointsWithoutAnOperation()
    {
        var (recorder, middleware) = CreateMiddleware(_ => Task.CompletedTask);
        using var _ = recorder;
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test"));

        await middleware.InvokeAsync(context);

        recorder.Measurements.Should().BeEmpty();
    }

    private static (AuthMetricsRecorder Recorder, AuthOperationMetricsMiddleware Middleware) CreateMiddleware(
        RequestDelegate next)
    {
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        var recorder = new AuthMetricsRecorder(meterFactory);
        return (recorder, new AuthOperationMetricsMiddleware(next, new AuthSecurityMetrics(meterFactory), TimeProvider.System));
    }

    private static DefaultHttpContext CreateContext(AuthOperation operation)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthOperationAttribute(operation)),
            "test"));
        return context;
    }
}
