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
using backend.main.shared.storage;

using Microsoft.Data.Sqlite;

namespace backend.tests.Integration.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _testConnectionString = $"Data Source=backend-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _connection;

    public InMemoryCacheService Cache { get; } = new();
    public CapturingPublisher Publisher { get; } = new();
    public FakeCaptchaService Captcha { get; } = new();
    public FakeOAuthService OAuth { get; } = new();
    public FakeFileUploadService Storage { get; } = new();
    public FakeAzureBlobService BlobStorage { get; } = new();

    public TestWebApplicationFactory()
    {
        _connection = new SqliteConnection(_testConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = _testConnectionString
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

            services.RemoveAll<IFileUploadService>();
            services.AddSingleton<IFileUploadService>(Storage);

            services.RemoveAll<IAzureBlobService>();
            services.AddSingleton<IAzureBlobService>(BlobStorage);
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
