using System.Text.RegularExpressions;

using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Infrastructure;

public sealed class MySqlTestDatabase : IAsyncDisposable
{
    private readonly IntegrationTestEnvironment _environment;

    private MySqlTestDatabase(
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

    public static async Task<MySqlTestDatabase> CreateAsync()
    {
        var environment = await IntegrationTestFixture.GetEnvironmentAsync();
        var databaseName = $"itest_{Guid.NewGuid():N}";
        ValidateDatabaseName(databaseName);
        var connectionString = environment.CreateDatabaseConnectionString(databaseName);

        await using (var admin = CreateAdminContext(environment))
        {
            var createSql = $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await admin.Database.ExecuteSqlRawAsync(createSql);
        }

        await using (var db = CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        return new MySqlTestDatabase(environment, databaseName, connectionString);
    }

    public AppDatabaseContext CreateDbContext() => CreateDbContext(ConnectionString);

    public async ValueTask DisposeAsync()
    {
        await using var admin = CreateAdminContext(_environment);
        var dropSql = $"DROP DATABASE IF EXISTS `{DatabaseName}`;";
        await admin.Database.ExecuteSqlRawAsync(dropSql);
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
            .UseMySql(
                connectionString,
                ServerVersion.AutoDetect(connectionString),
                mySqlOptions => mySqlOptions.EnableRetryOnFailure())
            .Options;

        return new AppDatabaseContext(options);
    }
}
