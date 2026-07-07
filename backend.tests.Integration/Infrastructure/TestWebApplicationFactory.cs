using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using backend.main.features.auth.captcha;
using backend.main.features.auth.oauth;
using backend.main.features.cache;
using backend.main.infrastructure.database.core;
using backend.main.infrastructure.redis;
using backend.main.shared.storage;

namespace backend.tests.Integration.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IntegrationTestEnvironment _environment;
    private readonly string _testConnectionString;
    private readonly Action<IServiceCollection>? _serviceOverrides;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    public FakeCaptchaService Captcha { get; } = new();
    public FakeOAuthService OAuth { get; } = new();
    public FakeAzureBlobService BlobStorage { get; } = new();

    public TestWebApplicationFactory(
        IntegrationTestEnvironment environment,
        string testConnectionString,
        Action<IServiceCollection>? serviceOverrides = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        _environment = environment;
        _testConnectionString = testConnectionString;
        _serviceOverrides = serviceOverrides;
        _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>(_configurationOverrides)
            {
                ["Database:Provider"] = "mysql",
                ["Database:ConnectionString"] = _testConnectionString
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<RedisHealth>();
            services.RemoveAll<ICacheService>();
            services.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
            services.RemoveAll<RedisReconnectState>();
            services.AddAppRedis(new ConfigurationBuilder().Build());

            services.RemoveAll<ICaptchaService>();
            services.AddSingleton<ICaptchaService>(Captcha);

            services.RemoveAll<IOAuthService>();
            services.AddSingleton<IOAuthService>(OAuth);

            services.RemoveAll<IAzureBlobService>();
            services.AddSingleton<IAzureBlobService>(BlobStorage);

            _serviceOverrides?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _environment.ResetSharedStateAsync().GetAwaiter().GetResult();
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        db.Database.Migrate();

        return host;
    }
}
