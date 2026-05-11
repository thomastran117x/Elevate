using backend.main.configurations.resource.elasticsearch;
using backend.main.publishers.implementation;
using backend.main.publishers.interfaces;
using backend.main.repositories.implementation;
using backend.main.repositories.interfaces;
using backend.main.services.implementation;
using backend.main.services.implementations;
using backend.main.services.interfaces;
using backend.main.seeders;
using backend.main.utilities.implementation;
using backend.main.utilities.interfaces;
using backend.main.infrastructure.database.repository;

namespace backend.main.application.bootstrap
{
    public static class Container
    {
        private static readonly Uri GoogleCaptchaBaseAddress = new("https://www.google.com/");

        public static IServiceCollection AddElasticsearchInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddAppElasticsearch(config);
            services.AddSingleton<ElasticsearchCircuitBreaker>();

            return services;
        }

        public static IServiceCollection AddEventSearchInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddElasticsearchInfrastructure(config);
            services.AddScoped<IEventSearchService, EventSearchService>();

            return services;
        }

        public static IServiceCollection AddClubPostSearchInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddElasticsearchInfrastructure(config);
            services.AddScoped<IClubPostSearchService, ClubPostSearchService>();

            return services;
        }

        public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            services.AddElasticsearchInfrastructure(config);
            services.AddScoped<IEventSearchService, EventSearchService>();
            services.AddScoped<IClubPostSearchService, ClubPostSearchService>();

            return services;
        }

        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddSearchInfrastructure(config);
            services.AddSingleton<IRepositoryResiliencePolicy, RepositoryResiliencePolicy>();
            services.AddSingleton<IRepositoryAttributeResolver, RepositoryAttributeResolver>();
            services.AddRepositoryWithProxy<IFollowRepository, FollowRepository>();
            services.AddRepositoryWithProxy<IUserRepository, UserRepository>();
            services.AddRepositoryWithProxy<IClubRepository, ClubRepository>();
            services.AddRepositoryWithProxy<IEventsRepository, EventsRepository>();
            services.AddRepositoryWithProxy<IPaymentRepository, PaymentRepository>();
            services.AddRepositoryWithProxy<IClubReviewRepository, ClubReviewRepository>();
            services.AddRepositoryWithProxy<IDeviceRepository, DeviceRepository>();
            services.AddRepositoryWithProxy<IClubPostRepository, ClubPostRepository>();
            services.AddRepositoryWithProxy<IPostCommentRepository, PostCommentRepository>();
            services.AddRepositoryWithProxy<IEventRegistrationRepository, EventRegistrationRepository>();
            services.AddRepositoryWithProxy<IEventAnalyticsRepository, EventAnalyticsRepository>();
            services.AddRepositoryWithProxy<IEventImageRepository, EventImageRepository>();

            services.AddSingleton<IPublisher, Publisher>();

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IOAuthService, OAuthService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IClubService, ClubService>();
            services.AddScoped<IFollowService, FollowService>();
            services.AddScoped<IEventsService, EventsService>();
            services.AddScoped<IPaymentService, StripePaymentService>();
            services.AddScoped<IClubReviewService, ClubReviewService>();
            services.AddScoped<IDeviceService, DeviceService>();
            services.AddScoped<IClubPostService, ClubPostService>();
            services.AddScoped<IClubPostReindexService, ClubPostReindexService>();
            services.AddHostedService<ElasticsearchIndexInitializationService>();
            services.AddScoped<IEventReindexService, EventReindexService>();
            services.AddScoped<IEventSearchOutboxWriter, EventSearchOutboxWriter>();
            services.AddScoped<IPostCommentService, PostCommentService>();
            services.AddScoped<IEventRegistrationService, EventRegistrationService>();
            services.AddScoped<IFileUploadService, FileUploadService>();
            services.AddScoped<IAzureBlobService, AzureBlobService>();

            services.AddSingleton<ICustomLogger, FileLogger>();

            services.AddHttpClient<GoogleCaptchaService>(client =>
            {
                client.BaseAddress = GoogleCaptchaBaseAddress;
            });
            services.AddHttpClient<CloudflareTurnstileCaptchaService>();
            services.AddScoped<ICaptchaService>(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();
                return ResolveCaptchaProvider(configuration) switch
                {
                    "turnstile" => provider.GetRequiredService<CloudflareTurnstileCaptchaService>(),
                    _ => provider.GetRequiredService<GoogleCaptchaService>(),
                };
            });

            services.AddAppSeeders();

            return services;
        }

        private static string ResolveCaptchaProvider(IConfiguration config)
        {
            var configuredProvider = (
                config["Captcha:Provider"]
                ?? config["CAPTCHA_PROVIDER"]
            )?.Trim().ToLowerInvariant();

            if (configuredProvider is "google" or "turnstile")
                return configuredProvider;

            var turnstileSecret =
                config["Turnstile:Secret"]
                ?? config["CLOUDFLARE_TURNSTILE_SECRET"]
                ?? config["TURNSTILE_SECRET"];

            return string.IsNullOrWhiteSpace(turnstileSecret) ? "google" : "turnstile";
        }
    }
}
