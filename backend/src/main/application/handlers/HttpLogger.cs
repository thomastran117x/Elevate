using System.Diagnostics;

using Serilog;
using Serilog.Events;

namespace backend.main.application.handlers
{
    public static class HttpLoggerHandler
    {
        public static IHostBuilder UseMinimalSerilog(this IHostBuilder host)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}")
                .CreateLogger();

            return host.UseSerilog();
        }
    }
    public class ResponseLoggerConfiguration
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ResponseLoggerConfiguration> _logger;

        public ResponseLoggerConfiguration(RequestDelegate next, ILogger<ResponseLoggerConfiguration> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            await _next(context);

            stopwatch.Stop();
            var responseTime = stopwatch.ElapsedMilliseconds;

            var statusCode = context.Response.StatusCode;
            var method = context.Request.Method;
            var path = context.Request.Path;

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = statusCode switch
            {
                >= 200 and < 300 => ConsoleColor.Green,
                >= 300 and < 400 => ConsoleColor.Cyan,
                >= 400 and < 500 => ConsoleColor.Yellow,
                >= 500 => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };

            var logMessage =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] " +
                $"{method} {path} → {statusCode} ({responseTime} ms)";

            _logger.LogInformation("{Method} {Path} → {StatusCode} ({Elapsed} ms)",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                responseTime);


            Console.ForegroundColor = originalColor;
        }
    }
}
