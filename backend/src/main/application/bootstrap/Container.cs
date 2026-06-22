using backend.main.application.features;
using backend.main.features.auth;
using backend.main.features.auth.captcha;
using backend.main.features.auth.device;
using backend.main.features.auth.mfa;
using backend.main.features.auth.notifications;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.comments;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.reviews;
using backend.main.features.clubs.search;
using backend.main.features.clubs.versions;
using backend.main.features.events;
using backend.main.features.events.analytics;
using backend.main.features.events.images;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.events.search;
using backend.main.features.events.versions;
using backend.main.features.payment;
using backend.main.features.profile;
using backend.main.infrastructure.database.repository;
using backend.main.infrastructure.elasticsearch;
using backend.main.seeders;
using backend.main.shared.providers;
using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

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
            var featureFlags = BuildFeatureFlagEvaluator(config);
            if (featureFlags.IsEnabled(FeatureFlagKeys.Search))
            {
                services.AddElasticsearchInfrastructure(config);
                services.AddScoped<IEventSearchService, EventSearchService>();
            }
            else
            {
                services.AddScoped<IEventSearchService, DisabledEventSearchService>();
            }

            return services;
        }

        public static IServiceCollection AddClubPostSearchInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            var featureFlags = BuildFeatureFlagEvaluator(config);
            if (featureFlags.IsEnabled(FeatureFlagKeys.Search))
            {
                services.AddElasticsearchInfrastructure(config);
                services.AddScoped<IClubPostSearchService, ClubPostSearchService>();
            }
            else
            {
                services.AddScoped<IClubPostSearchService, DisabledClubPostSearchService>();
            }

            return services;
        }

        public static IServiceCollection AddClubSearchInfrastructure(this IServiceCollection services, IConfiguration config)
        {
            var featureFlags = BuildFeatureFlagEvaluator(config);
            if (featureFlags.IsEnabled(FeatureFlagKeys.Search))
            {
                services.AddElasticsearchInfrastructure(config);
                services.AddScoped<IClubSearchService, ClubSearchService>();
            }
            else
            {
                services.AddScoped<IClubSearchService, DisabledClubSearchService>();
            }

            return services;
        }

        public static IServiceCollection AddSearchInfrastructure(this IServiceCollection services, IConfiguration config, IFeatureFlagEvaluator? featureFlags = null)
        {
            featureFlags ??= BuildFeatureFlagEvaluator(config);

            if (featureFlags.IsEnabled(FeatureFlagKeys.Search))
            {
                services.AddElasticsearchInfrastructure(config);
                services.AddScoped<IEventSearchService, EventSearchService>();
                services.AddScoped<IClubSearchService, ClubSearchService>();
                services.AddScoped<IClubPostSearchService, ClubPostSearchService>();
                services.AddScoped<IEventSearchOutboxWriter, EventSearchOutboxWriter>();
                services.AddScoped<IClubSearchOutboxWriter, ClubSearchOutboxWriter>();
                services.AddScoped<IClubPostSearchOutboxWriter, ClubPostSearchOutboxWriter>();
            }
            else
            {
                services.AddScoped<IEventSearchService, DisabledEventSearchService>();
                services.AddScoped<IClubSearchService, DisabledClubSearchService>();
                services.AddScoped<IClubPostSearchService, DisabledClubPostSearchService>();
                services.AddScoped<IEventSearchOutboxWriter, DisabledEventSearchOutboxWriter>();
                services.AddScoped<IClubSearchOutboxWriter, DisabledClubSearchOutboxWriter>();
                services.AddScoped<IClubPostSearchOutboxWriter, DisabledClubPostSearchOutboxWriter>();
            }

            if (featureFlags.IsEnabled(FeatureFlagKeys.SearchReindex))
            {
                services.AddScoped<IClubPostReindexService, ClubPostReindexService>();
                services.AddScoped<IClubReindexService, ClubReindexService>();
                services.AddScoped<IEventReindexService, EventReindexService>();
            }
            else
            {
                services.AddScoped<IClubPostReindexService, DisabledClubPostReindexService>();
                services.AddScoped<IClubReindexService, DisabledClubReindexService>();
                services.AddScoped<IEventReindexService, DisabledEventReindexService>();
            }

            return services;
        }

        public static IServiceCollection AddApplicationServices(
            this IServiceCollection services,
            IConfiguration config,
            bool includeHostedServices = true)
        {
            var featureFlags = BuildFeatureFlagEvaluator(config);

            services.Configure<ClubVersioningOptions>(config.GetSection("ClubVersioning"));
            services.Configure<EventVersioningOptions>(config.GetSection("EventVersioning"));
            services.AddSingleton(TimeProvider.System);
            services.AddSearchInfrastructure(config, featureFlags);
            services.AddSingleton<IRepositoryResiliencePolicy, RepositoryResiliencePolicy>();
            services.AddSingleton<IRepositoryAttributeResolver, RepositoryAttributeResolver>();
            services.AddRepositoryWithProxy<IFollowRepository, FollowRepository>();
            services.AddRepositoryWithProxy<IAuthUserRepository, AuthUserRepository>();
            services.AddRepositoryWithProxy<IUserRepository, AuthUserRepository>();
            services.AddRepositoryWithProxy<IMfaEnrollmentRepository, MfaEnrollmentRepository>();
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
            services.AddScoped<IAuthNotificationService, AuthNotificationService>();

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IMfaEnrollmentService, MfaEnrollmentService>();
            services.AddScoped<IOAuthService, OAuthService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IClubService, ClubService>();
            services.AddScoped<ClubVersionCleanupRunner>();
            services.AddScoped<IClubReviewService, ClubReviewService>();
            services.AddScoped<IDeviceService, DeviceService>();
            services.AddScoped<IClubPostService, ClubPostService>();
            services.AddSingleton<CommentEventBroker>();
            services.AddSingleton<IRefreshAheadCache, RefreshAheadCache>();
            services.AddScoped<IAzureBlobService, AzureBlobService>();

            if (featureFlags.IsEnabled(FeatureFlagKeys.ClubsFollow))
                services.AddScoped<IFollowService, FollowService>();
            else
                services.AddScoped<IFollowService, DisabledFollowService>();

            services.AddScoped<IEventsService, EventsService>();

            if (featureFlags.IsEnabled(FeatureFlagKeys.EventsInvitations))
            {
                services.AddScoped<IEventInvitationService, EventInvitationService>();
                services.AddSingleton(EventInvitationStatusConsumerOptions.FromEnvironment());
            }
            else
            {
                services.AddScoped<IEventInvitationService, DisabledEventInvitationService>();
            }

            if (featureFlags.IsEnabled(FeatureFlagKeys.Payment))
                services.AddScoped<IPaymentService, StripePaymentService>();
            else
                services.AddScoped<IPaymentService, DisabledPaymentService>();

            if (featureFlags.IsEnabled(FeatureFlagKeys.EventsRegistration))
                services.AddScoped<IEventRegistrationService, EventRegistrationService>();
            else
                services.AddScoped<IEventRegistrationService, DisabledEventRegistrationService>();

            services.AddScoped<IPostCommentService, PostCommentService>();

            if (includeHostedServices)
            {
                if (featureFlags.IsEnabled(FeatureFlagKeys.Search))
                    services.AddHostedService<ElasticsearchIndexInitializationService>();

                if (featureFlags.IsEnabled(FeatureFlagKeys.ClubsVersioning))
                    services.AddHostedService<ClubVersionCleanupService>();

                if (featureFlags.IsEnabled(FeatureFlagKeys.EventsInvitations))
                    services.AddHostedService<EventInvitationStatusConsumer>();
            }

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

        private static IFeatureFlagEvaluator BuildFeatureFlagEvaluator(IConfiguration config)
        {
            var registry = FeatureFlagRegistry.Instance;
            return new FeatureFlagEvaluator(
                Options.Create(FeatureFlagsOptions.FromConfiguration(config, registry)),
                registry);
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

