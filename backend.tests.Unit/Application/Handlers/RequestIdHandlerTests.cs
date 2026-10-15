using backend.main.application.handlers;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Application.Handlers;

public class RequestIdHandlerTests
{
    [Fact]
    public async Task UseRequestId_ShouldGenerateAndEchoRequestId_WhenMissing()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRequestId();
        app.Run(async context =>
        {
            await context.Response.StartAsync();
            await context.Response.WriteAsync("ok");
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        context.Request.Headers["X-Request-Id"].ToString().Should().NotBeNullOrWhiteSpace();
        context.TraceIdentifier.Should().Be(context.Request.Headers["X-Request-Id"].ToString());
    }

    [Fact]
    public async Task UseRequestId_ShouldPreserveExistingHeader()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRequestId();
        app.Run(async context =>
        {
            await context.Response.StartAsync();
            await context.Response.WriteAsync("ok");
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Request-Id"] = "request-123";

        await pipeline(context);

        context.TraceIdentifier.Should().Be("request-123");
    }

    [Theory]
    [InlineData("forged\r\nline")]
    [InlineData("<script>")]
    [InlineData("with space")]
    public async Task UseRequestId_ShouldReplaceAnUnsafeHeader(string supplied)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRequestId();
        app.Run(_ => Task.CompletedTask);

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Request-Id"] = supplied;

        await pipeline(context);

        context.TraceIdentifier.Should().NotBe(supplied);
        Guid.TryParse(context.TraceIdentifier, out _).Should().BeTrue();
    }

    [Fact]
    public void IsValidRequestId_ShouldRejectOverlongValues()
    {
        RequestIdHandler.IsValidRequestId(new string('a', 128)).Should().BeTrue();
        RequestIdHandler.IsValidRequestId(new string('a', 129)).Should().BeFalse();
        RequestIdHandler.IsValidRequestId(null).Should().BeFalse();
    }
}
