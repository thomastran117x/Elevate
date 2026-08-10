using System.Text.RegularExpressions;

using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Infrastructure;

public sealed class PostgresTestDatabase : IAsyncDisposable
{
    internal const string TemplateDatabaseName = "itest_template";
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
            // Built into a local first: passing an interpolated string straight to
            // ExecuteSqlRawAsync trips EF1002. The name is validated above.
            var createSql =
                $"CREATE DATABASE \"{databaseName}\" WITH TEMPLATE \"{TemplateDatabaseName}\";";
            await admin.Database.ExecuteSqlRawAsync(createSql);
        }

        return new PostgresTestDatabase(environment, databaseName, connectionString);
    }

    internal static async Task InitializeTemplateAsync(IntegrationTestEnvironment environment)
    {
        await using (var admin = CreateAdminContext(environment))
        {
            var createSql =
                $"CREATE DATABASE \"{TemplateDatabaseName}\";";
            await admin.Database.ExecuteSqlRawAsync(createSql);
        }

        await using var template = CreateDbContext(
            environment.CreateDatabaseConnectionString(TemplateDatabaseName));
        await template.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        const string resetSql = """
            DO $$
            DECLARE
                table_names text;
            BEGIN
                SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
                  INTO table_names
                  FROM pg_tables
                 WHERE schemaname = 'public'
                   AND tablename <> '__EFMigrationsHistory';

                IF table_names IS NOT NULL THEN
                    EXECUTE 'TRUNCATE TABLE ' || table_names || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            """;
        await db.Database.ExecuteSqlRawAsync(resetSql);
    }

    public AppDatabaseContext CreateDbContext() => CreateDbContext(ConnectionString);

    public async ValueTask DisposeAsync()
    {
        await using var admin = CreateAdminContext(_environment);

        // WITH (FORCE) terminates any lingering backends. PostgreSQL refuses to drop a
        // database that still has live connections, which makes teardown flaky otherwise.
        var dropSql = $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE);";
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
            .UseNpgsql(connectionString)
            .Options;

        return new AppDatabaseContext(options);
    }
}
