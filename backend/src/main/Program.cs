using backend.main.configurations.application;
using backend.main.configurations.environment;
using backend.main.configurations.resource.database;
using backend.main.configurations.resource.redis;
using backend.main.configurations.security;
using backend.main.seeders;
using backend.main.utilities.implementation;
using backend.main.utilities.interfaces;
using Serilog;

Logger.Configure(o =>
{
    o.EnableFileLogging = true;
    o.MinFileLevel = backend.main.utilities.interfaces.LogLevel.Warn;
    o.LogDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs")
    );
});

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(Logger.GetOptions());

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB
});

var port = builder.ConfigureServerUrls();

EnvironmentSetting.Validate();

builder.Host.UseMinimalSerilog();

builder.Services.AddControllersWithViews(options =>
{
    options.Conventions.Insert(0, new RoutePrefixConvention(RoutePaths.ApiPrefix));
});

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddClientRequestInspection();

builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddAppRedis(builder.Configuration);

builder.Services.AddJwtAuth(builder.Configuration);
builder.Services.AddCustomCors(builder.Configuration);
builder.Services.AddCustomCsrf();
builder.Services.AddInMemoryRateLimiter();
builder.Services.AddCustomRequestTimeouts();
builder.Services.AddForwardedHeaders(builder.Configuration);

builder.Services.AddWebConfiguration(builder.Configuration);

var app = builder.Build();

Logger.SetInstance(app.Services.GetRequiredService<ICustomLogger>());

await DatabaseConfig.VerifyDatabaseConnectionAsync(app.Services);
await DatabaseConfig.EnsureDatabaseMigratedAsync(app.Services);
await app.Services.SeedAppDataAsync();

app.UseForwardedHeaders();

app.UseMiddleware<GlobalExceptionMiddleware>();

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

app.MapControllers();

app.UseJsonNotFound();

var redisHealth = app.Services.GetRequiredService<RedisHealth>();

Logger.Info($"Server is listening on port {port}");

app.Run();
