namespace backend.main.seeders
{
    public static class SeederConfiguration
    {
        public static IServiceCollection AddAppSeeders(this IServiceCollection services)
        {
            services.AddScoped<ISeederOrchestrator, SeederOrchestrator>();
            services.AddScoped<ISeeder, AuthUsersSeeder>();
            services.AddScoped<ISeeder, MockClubSeeder>();
            services.AddScoped<ISeeder, MockEventSeeder>();
            return services;
        }

        public static async Task SeedAppDataAsync(
            this IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default
        )
        {
            using var scope = serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ISeederOrchestrator>();
            await orchestrator.RunAsync(cancellationToken);
        }
    }
}
