using backend.main.application.security;
using backend.main.features.auth.events;

using backend.tests.Unit.Features.Auth.Events;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Application.Security;

public class AuthOperationAttributeTests
{
    [Theory]
    [InlineData(StatusCodes.Status200OK, false, AuthSecurityOutcome.Succeeded)]
    [InlineData(StatusCodes.Status302Found, false, AuthSecurityOutcome.Succeeded)]
    [InlineData(StatusCodes.Status401Unauthorized, false, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status409Conflict, false, AuthSecurityOutcome.Denied)]
    [InlineData(StatusCodes.Status429TooManyRequests, false, AuthSecurityOutcome.Throttled)]
    [InlineData(StatusCodes.Status503ServiceUnavailable, false, AuthSecurityOutcome.Faulted)]
    [InlineData(StatusCodes.Status200OK, true, AuthSecurityOutcome.Faulted)]
    public void Classify_ShouldSeparateRefusalsFromFaults(int statusCode, bool faulted, AuthSecurityOutcome expected)
    {
        AuthOperationAttribute.Classify(statusCode, faulted).Should().Be(expected);
    }

    [Fact]
    public async Task Filter_ShouldRecordTheOperationAndItsOutcome()
    {
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var (context, executing) = CreateContext(new AuthSecurityMetrics(meterFactory));
        var attribute = new AuthOperationAttribute(AuthOperation.SignIn);

        await attribute.OnResourceExecutionAsync(executing, () =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.FromResult(new ResourceExecutedContext(executing, []));
        });

        var measurement = recorder.Measurements.Should().ContainSingle().Subject;
        measurement.Instrument.Should().Be("auth.operation.duration");
        measurement.Tags["operation"].Should().Be("sign_in");
        measurement.Tags["outcome"].Should().Be("denied");
    }

    [Fact]
    public async Task Filter_ShouldRecordAFault_WhenTheActionThrows()
    {
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var (_, executing) = CreateContext(new AuthSecurityMetrics(meterFactory));
        var attribute = new AuthOperationAttribute(AuthOperation.Refresh);

        var act = () => attribute.OnResourceExecutionAsync(
            executing,
            () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        recorder.Measurements.Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("faulted");
    }

    [Fact]
    public async Task Filter_ShouldNotRecordARequestTheClientAbandoned()
    {
        var meterFactory = AuthSecurityEventTestSupport.CreateMeterFactory();
        using var recorder = new AuthMetricsRecorder(meterFactory);
        var (context, executing) = CreateContext(new AuthSecurityMetrics(meterFactory));
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        context.RequestAborted = aborted.Token;
        var attribute = new AuthOperationAttribute(AuthOperation.SignIn);

        var act = () => attribute.OnResourceExecutionAsync(
            executing,
            () => throw new OperationCanceledException(aborted.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
        recorder.Measurements.Should().BeEmpty();
    }

    private static (DefaultHttpContext Context, ResourceExecutingContext Executing) CreateContext(
        AuthSecurityMetrics metrics)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(metrics)
                .AddSingleton(TimeProvider.System)
                .BuildServiceProvider(),
        };
        var actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());
        return (context, new ResourceExecutingContext(actionContext, [], []));
    }
}
