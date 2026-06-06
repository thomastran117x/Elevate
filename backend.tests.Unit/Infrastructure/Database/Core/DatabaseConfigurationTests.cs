using backend.main.infrastructure.database.core;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Infrastructure.Database.Core;

public class DatabaseConfigurationTests
{
    [Fact]
    public void AddAppDatabase_ShouldConfigureOpenApiSqliteDatabase_WhenInMemoryFlagEnabled()
    {
        var services = new ServiceCollection();

        services.AddAppDatabase(new ConfigurationBuilder().Build(), useInMemorySqlite: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        db.Database.GetDbConnection().ConnectionString.Should().Contain("Data Source=openapi-docs");
    }

    [Fact]
    public void AddAppDatabase_ShouldConfigureSqliteProvider_WhenSelectedInConfig()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["Database:ConnectionString"] = "Data Source=:memory:"
            })
            .Build();

        services.AddAppDatabase(config);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        db.Database.GetDbConnection().ConnectionString.Should().Be("Data Source=:memory:");
    }

    [Fact]
    public async Task VerifyDatabaseConnectionAsync_ShouldSucceed_ForConfiguredSqlite()
    {
        var services = new ServiceCollection();
        services.AddAppDatabase(new ConfigurationBuilder().Build(), useInMemorySqlite: true);

        using var provider = services.BuildServiceProvider();

        await DatabaseConfig.VerifyDatabaseConnectionAsync(provider);
    }
}
