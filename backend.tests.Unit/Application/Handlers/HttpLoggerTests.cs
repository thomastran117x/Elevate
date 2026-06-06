using backend.main.application.handlers;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace backend.tests.Unit.Application.Handlers;

public class HttpLoggerTests
{
    [Fact]
    public void UseMinimalSerilog_ShouldReturnHostBuilder()
    {
        using var host = HttpLoggerHandler
            .UseMinimalSerilog(new HostBuilder())
            .Build();

        Log.Logger.Should().NotBeNull();
    }

    [Theory]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(0)]
    public async Task InvokeAsync_ShouldLogRequestStatus_AndRestoreConsoleColor(int statusCode)
    {
        var originalColor = Console.ForegroundColor;
        var logger = new ListLogger<ResponseLoggerConfiguration>();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/health";

        var middleware = new ResponseLoggerConfiguration(
            async httpContext =>
            {
                httpContext.Response.StatusCode = statusCode;
                await Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        logger.Messages.Should().ContainSingle(message =>
            message.Contains("GET") &&
            message.Contains("/health") &&
            message.Contains(statusCode.ToString()));
        Console.ForegroundColor.Should().Be(originalColor);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
