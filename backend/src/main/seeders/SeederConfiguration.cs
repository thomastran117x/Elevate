namespace backend.main.seeders
{
    public static class SeederConfiguration
    {
        public static IServiceCollection AddAppSeeders(this IServiceCollection services)
        {
            services.AddScoped<IClubSeedDefinitionSource, clubs.HarbourStridersClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.SummitTrailSocietyClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.NorthCampusBuildersClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.CivicSpeakersForumClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.LanternSocialCollectiveClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.WeekendMakersCommonsClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.MosaicArtsCircleClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.RhythmExchangeCollectiveClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.PixelPlayGuildClubSeed>();
            services.AddScoped<IClubSeedDefinitionSource, clubs.NeighbourhoodKitchenTableClubSeed>();
            services.AddScoped<ISeederOrchestrator, SeederOrchestrator>();
            services.AddScoped<ISeeder, SeedUsersSeeder>();
            services.AddScoped<ISeeder, SeedClubContentSeeder>();
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
