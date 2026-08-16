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
using backend.main.infrastructure.elasticsearch;
using backend.main.infrastructure.redis;
using backend.main.shared.providers;
using backend.main.shared.storage;

namespace backend.tests.Integration.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IntegrationTestEnvironment _environment;
    private readonly string _testConnectionString;
    private readonly Action<IServiceCollection>? _serviceOverrides;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly TestResourceNamespace _resources;

    public FakeCaptchaService Captcha { get; } = new();
    public FakeOAuthService OAuth { get; } = new();
    public FakeAzureBlobService BlobStorage { get; } = new();

    public TestWebApplicationFactory(
        IntegrationTestEnvironment environment,
        string testConnectionString,
        TestResourceNamespace resources,
        Action<IServiceCollection>? serviceOverrides = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        _environment = environment;
        _testConnectionString = testConnectionString;
        _resources = resources;
        _serviceOverrides = serviceOverrides;
        _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>(_configurationOverrides)
            {
                ["Database:Provider"] = "postgres",
                ["Database:ConnectionString"] = _testConnectionString,
                ["Redis:ConnectionString"] = _environment.CreateRedisConnectionString(_resources.RedisDatabase),
                ["Elasticsearch:Url"] = _environment.ElasticsearchUrl,
                [SearchIndexNames.EventsConfigurationKey] = _resources.EventsIndex,
                [SearchIndexNames.ClubsConfigurationKey] = _resources.ClubsIndex,
                [SearchIndexNames.ClubPostsConfigurationKey] = _resources.ClubPostsIndex,
                ["RateLimiter:PermitLimit"] = "100000",
                ["RateLimiter:AuthPermitLimit"] = "100000"
            };

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices((context, services) =>
        {
            services.RemoveAll<AppDatabaseContext>();
            services.RemoveAll<DbContextOptions<AppDatabaseContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<AppDatabaseContext>>();

            // NOTE: this deliberately omits the EnableRetryOnFailure that
            // DatabaseConfiguration applies in production. Without the retrying execution
            // strategy, EF permits user-initiated transactions that production rejects, so
            // code calling BeginTransactionAsync outside CreateExecutionStrategy passes here
            // and then throws a 500 for real. Aligning the two is the right fix, but 26 call
            // sites across events, series, registration, waitlist, invitations and payments
            // still need wrapping first — see the follow-up issue.
            var dbContextOptions = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseNpgsql(_testConnectionString)
                .Options;

            services.AddSingleton(dbContextOptions);
            services.AddScoped(_ => new AppDatabaseContext(dbContextOptions));

            services.RemoveAll<RedisHealth>();
            services.RemoveAll<ICacheService>();
            services.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
            services.RemoveAll<RedisReconnectState>();
            services.AddAppRedis(context.Configuration);

            services.RemoveAll<ICaptchaService>();
            services.AddSingleton<ICaptchaService>(Captcha);

            services.RemoveAll<IOAuthService>();
            services.AddSingleton<IOAuthService>(OAuth);

            services.RemoveAll<IAzureBlobService>();
            services.AddSingleton<IAzureBlobService>(BlobStorage);

            services.RemoveAll<IPublisher>();
            services.AddSingleton<IPublisher>(_ =>
                new NamespacedKafkaPublisher(_environment, _resources));

            _serviceOverrides?.Invoke(services);
        });
    }

    public void ResetTestDoubles()
    {
        Captcha.ShouldSucceed = true;
        OAuth.Clear();
        BlobStorage.Clear();
    }
}

