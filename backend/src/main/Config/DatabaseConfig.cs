using backend.main.Resources;
using backend.main.Utilities;

using Microsoft.EntityFrameworkCore;

using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace backend.main.Config
{
    public static class DatabaseConfig
    {
        public static IServiceCollection AddAppDatabase(
            this IServiceCollection services,
            IConfiguration config)
        {
            var connectionString = EnvManager.DbConnectionString;

            services.AddDbContext<AppDatabaseContext>(options =>
            {
                options.UseMySql(
                    connectionString,
                    ServerVersion.AutoDetect(connectionString),
                    mySqlOptions =>
                    {
                        mySqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null
                        );
                    });
            });

            return services;
        }

        public static async Task VerifyDatabaseConnectionAsync(
            IServiceProvider serviceProvider)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

                await db.Database.OpenConnectionAsync();
                await db.Database.CloseConnectionAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Failed to connect to database after retries: {ex.Message}"
                );

                Environment.Exit(1);
            }
        }
    }
}
