using backend.main.configurations.application;
using backend.main.configurations.resource.database;
using backend.main.configurations.resource.redis;
using backend.main.configurations.security;
using backend.main.utilities.implementation;
using backend.main.utilities.interfaces;

using Serilog;

Logger.Configure(o =>
{
    o.EnableFileLogging = true;
    o.MinFileLevel = backend.main.utilities.interfaces.LogLevel.Warn;
    o.LogDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs"));
});

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(Logger.GetOptions());

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var port = builder.ConfigureServerUrls();

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
builder.Services.AddCustomCors(builder.Configuration);
builder.Services.AddCustomCsrf();
builder.Services.AddInMemoryRateLimiter();

builder.Services.AddWebConfiguration(builder.Configuration);

var app = builder.Build();

Logger.SetInstance(app.Services.GetRequiredService<ICustomLogger>());

await DatabaseConfig.VerifyDatabaseConnectionAsync(app.Services);

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseRouting();

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

app.UseRefreshCsrfCookie();
app.UseRefreshCsrfValidation();

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

app.UseJsonNotFound();

var redisHealth = app.Services.GetRequiredService<RedisHealth>();

Logger.Info($"Server is listening on port {port}");

app.Run();
