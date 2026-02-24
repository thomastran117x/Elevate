using System.Threading.RateLimiting;

using backend.main.Config;
using backend.main.Middlewares;
using backend.main.Resources;
using backend.main.Utilities;

using Microsoft.AspNetCore.RateLimiting;

using Serilog;

Logger.Configure(o =>
{
    o.EnableFileLogging = true;
    o.MinFileLevel = backend.main.Utilities.LogLevel.Warn;
    o.LogDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs"));
});

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();
var port =
    Environment.GetEnvironmentVariable("PORT") ??
    Environment.GetEnvironmentVariable("ASPNETCORE_PORT") ??
    "8090";

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Host.UseMinimalSerilog();
builder.Services.AddControllersWithViews(options =>
{
    options.Conventions.Insert(0, new RoutePrefixConvention("api"));
});

builder.Services.AddApplicationServices();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddAppRedis(builder.Configuration);

builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddCustomCors();
builder.Services.AddAppRateLimiter(new RateLimitOptions
{
    Strategy = RateLimitStrategy.TokenBucket,
    TokenLimit = 10,
    TokensPerPeriod = 10,
    ReplenishmentPeriod = TimeSpan.FromSeconds(30)
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("local-fallback", context =>
    {
        var key =
            context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("sub")?.Value ?? "anon"
                : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetTokenBucketLimiter(
            key,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,
                TokensPerPeriod = 10,
                ReplenishmentPeriod = TimeSpan.FromSeconds(30),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
});

var app = builder.Build();

await DatabaseConfig.VerifyDatabaseConnectionAsync(app.Services);

// app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseRouting();
app.UseCors("AllowFrontend");

app.UseWhen(
    ctx => !ctx.RequestServices.GetRequiredService<RedisHealth>().IsAvailable,
    branch =>
    {
        branch.UseRateLimiter(new RateLimiterOptions
        {
            GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var key =
                    httpContext.User.Identity?.IsAuthenticated == true
                        ? httpContext.User.FindFirst("sub")?.Value ?? "anon"
                        : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetTokenBucketLimiter(
                    key,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 10,
                        TokensPerPeriod = 10,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(30),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            })
        });
    }
);

app.UseMiddleware<RedisRateLimitMiddleware>();
app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    opts.EnrichDiagnosticContext = (ctx, http) =>
    {
        ctx.Set("RequestHost", http.Request.Host.Value!);
        ctx.Set("RequestScheme", http.Request.Scheme);
        ctx.Set("UserAgent", http.Request.Headers.UserAgent.ToString());
    };
});

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();

app.MapGet("/api", () =>
{
    return Results.Json(new
    {
        status = "Healthy",
        timestamp = DateTime.UtcNow
    });
});

app.MapGet("/health", () =>
{
    return Results.Json(new
    {
        status = "Healthy",
        timestamp = DateTime.UtcNow
    });
});

app.Use(async (context, next) =>
{
    await next();

    if (context.Response.StatusCode == StatusCodes.Status404NotFound &&
        !context.Response.HasStarted)
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Resource not found",
            code = 404,
            path = context.Request.Path
        });
    }
});

var redisHealth = app.Services.GetRequiredService<RedisHealth>();

Logger.Info($"Server is listening on port {port}");

app.Run();
