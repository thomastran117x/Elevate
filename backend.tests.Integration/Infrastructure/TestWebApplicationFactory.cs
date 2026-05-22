using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using backend.main.features.auth.captcha;
using backend.main.features.auth.oauth;
using backend.main.features.cache;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers;

using Microsoft.Data.Sqlite;

namespace backend.tests.Integration.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString = "Data Source=backend-auth-tests;Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _connection = new(TestConnectionString);

    public InMemoryCacheService Cache { get; } = new();
    public CapturingPublisher Publisher { get; } = new();
    public FakeCaptchaService Captcha { get; } = new();
    public FakeOAuthService OAuth { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = TestConnectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<AppDatabaseContext>>();
            services.RemoveAll<DbContextOptions<AppDatabaseContext>>();
            services.RemoveAll<AppDatabaseContext>();
            services.AddDbContext<AppDatabaseContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<ICacheService>();
            services.AddSingleton<ICacheService>(Cache);

            services.RemoveAll<IPublisher>();
            services.AddSingleton<IPublisher>(Publisher);

            services.RemoveAll<ICaptchaService>();
            services.AddSingleton<ICaptchaService>(Captcha);

            services.RemoveAll<IOAuthService>();
            services.AddSingleton<IOAuthService>(OAuth);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        _connection.Open();
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _connection.Dispose();
    }
}
