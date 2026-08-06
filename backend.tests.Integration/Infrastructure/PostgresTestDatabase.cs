using System.Text.RegularExpressions;

using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Infrastructure;

public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly IntegrationTestEnvironment _environment;

    private PostgresTestDatabase(
        IntegrationTestEnvironment environment,
        string databaseName,
        string connectionString)
    {
        _environment = environment;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var environment = await IntegrationTestFixture.GetEnvironmentAsync();
        var databaseName = $"itest_{Guid.NewGuid():N}";
        ValidateDatabaseName(databaseName);
        var connectionString = environment.CreateDatabaseConnectionString(databaseName);

        await using (var admin = CreateAdminContext(environment))
        {
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{databaseName}\";");
        }

        await using (var db = CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        return new PostgresTestDatabase(environment, databaseName, connectionString);
    }

    public AppDatabaseContext CreateDbContext() => CreateDbContext(ConnectionString);

    public async ValueTask DisposeAsync()
    {
        await using var admin = CreateAdminContext(_environment);

        // WITH (FORCE) terminates any lingering backends. PostgreSQL refuses to drop a
        // database that still has live connections, which makes teardown flaky otherwise.
        await admin.Database.ExecuteSqlRawAsync(
            $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);");
    }

    private static AppDatabaseContext CreateAdminContext(IntegrationTestEnvironment environment) =>
        CreateDbContext(environment.CreateDatabaseConnectionString("appdb"));

    private static void ValidateDatabaseName(string databaseName)
    {
        if (!Regex.IsMatch(databaseName, "^itest_[a-f0-9]{32}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"Unexpected integration test database name '{databaseName}'.");
    }

    private static AppDatabaseContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDatabaseContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDatabaseContext(options);
    }
}
