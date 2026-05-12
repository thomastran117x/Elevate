using backend.main.features.auth;
using backend.main.features.auth.captcha;
using backend.main.features.auth.device;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.reviews;
using backend.main.features.events;
using backend.main.features.events.analytics;
using backend.main.features.events.images;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.payment;
using backend.main.features.profile;
using backend.main.seeders;
using backend.main.infrastructure.database.repository;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.storage;
using backend.main.shared.providers;
using backend.main.shared.utilities.logger;

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
            services.AddRepositoryWithProxy<IAuthUserRepository, AuthUserRepository>();
            services.AddRepositoryWithProxy<IUserRepository, AuthUserRepository>();
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
