using backend.main.application.bootstrap;
using backend.main.application.environment;
using backend.main.application.features;
using backend.main.application.handlers;
using backend.main.application.openapi;
using backend.main.application.security;
using backend.main.features.cache;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.redis;
using backend.main.seeders;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

using Serilog;

Logger.Configure(o =>
{
    o.EnableFileLogging = true;
    o.MinFileLevel = backend.main.shared.utilities.logger.LogLevel.Warn;
    o.LogDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs")
    );
});

var builder = WebApplication.CreateBuilder(args);
var isTesting = builder.Environment.IsEnvironment("Testing");
var isOpenApiDocumentMode = OpenApiDocumentMode.ShouldSkipStartupSideEffects;
var suppressStartupSideEffects = isTesting || isOpenApiDocumentMode;
var featureFlagRegistry = FeatureFlagRegistry.Instance;
var featureFlagOptions = FeatureFlagsOptions.FromConfiguration(builder.Configuration, featureFlagRegistry);
var featureFlagEvaluator = new FeatureFlagEvaluator(Options.Create(featureFlagOptions), featureFlagRegistry);

builder.Services.AddSingleton(Logger.GetOptions());
builder.Services.AddFeatureFlags(builder.Configuration);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB
});

var port = builder.ConfigureServerUrls();

if (!isOpenApiDocumentMode)
{
    EnvironmentSetting.Validate();
}

builder.Host.UseMinimalSerilog();

builder.Services.AddControllersWithViews(options =>
{
    options.Conventions.Insert(0, new RoutePrefixConvention(RoutePaths.ApiPrefix));
    options.Conventions.Insert(1, new FeatureGateConvention(featureFlagEvaluator));
});
builder.Services.AddApiResponseConventions();
builder.Services.AddAppOpenApi();

builder.Services.AddApplicationServices(
    builder.Configuration,
    includeHostedServices: !suppressStartupSideEffects
);
builder.Services.AddHttpContextAccessor();
builder.Services.AddClientRequestInspection();

builder.Services.AddAppDatabase(
    builder.Configuration,
    useInMemorySqlite: isOpenApiDocumentMode
);
if (suppressStartupSideEffects)
{
    builder.Services.AddSingleton(new RedisHealth());
    builder.Services.AddSingleton<ICacheService, NoOpCacheService>();
}
else
{
    builder.Services.AddAppRedis(builder.Configuration);
}

builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddCustomCors(builder.Configuration);
builder.Services.AddCustomCsrf();
builder.Services.AddInMemoryRateLimiter();
builder.Services.AddCustomRequestTimeouts();
builder.Services.AddForwardedHeaders(builder.Configuration);

builder.Services.AddWebConfiguration(builder.Configuration);

var app = builder.Build();

Logger.SetInstance(app.Services.GetRequiredService<ICustomLogger>());

if (!suppressStartupSideEffects)
{
    await DatabaseConfig.VerifyDatabaseConnectionAsync(app.Services);
    await DatabaseConfig.EnsureDatabaseMigratedAsync(app.Services);
    await app.Services.SeedAppDataAsync();
}

app.UseForwardedHeaders();

app.UseMiddleware<GlobalExceptionHandler>();

app.UseRequestId();

app.UseRouting();

app.UseRequestTimeouts();

app.UseRateLimiter();

app.UseSecurityHeaders();
app.UseHttpsEnforcement();

app.UseCors(CorsConfiguration.DefaultPolicyName);

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

app.UseRefreshCsrfValidation();

app.UseAuthentication();
app.UseAuthorization();

app.UseClientRequestInspection();

app.MapAppOpenApi();

app.MapControllers();

app.UseJsonNotFound();

var redisHealth = app.Services.GetRequiredService<RedisHealth>();

Logger.Info($"Server is listening on port {port}");

app.Run();

public partial class Program;
