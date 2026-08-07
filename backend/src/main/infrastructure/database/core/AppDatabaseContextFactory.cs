using backend.main.application.environment;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace backend.main.infrastructure.database.core;

public sealed class AppDatabaseContextFactory : IDesignTimeDbContextFactory<AppDatabaseContext>
{
    public AppDatabaseContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDatabaseContext>();
        var connectionString = EnvironmentSetting.DbConnectionString;

        optionsBuilder.UseNpgsql(connectionString);

        return new AppDatabaseContext(optionsBuilder.Options);
    }
}
