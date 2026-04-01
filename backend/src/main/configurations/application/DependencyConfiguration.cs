using backend.main.attributes.repository;
using backend.main.publishers.implementation;
using backend.main.publishers.interfaces;
using backend.main.repositories.extensions;
using backend.main.repositories.implementation;
using backend.main.repositories.interfaces;
using backend.main.repositories.resilience;
using backend.main.services.implementation;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;
using backend.main.utilities.interfaces;

namespace backend.main.configurations.application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddSingleton<IRepositoryResiliencePolicy, RepositoryResiliencePolicy>();
            services.AddSingleton<IRepositoryAttributeResolver, RepositoryAttributeResolver>();
            services.AddRepositoryWithProxy<IFollowRepository, FollowRepository>();
            services.AddRepositoryWithProxy<IUserRepository, UserRepository>();
            services.AddRepositoryWithProxy<IClubRepository, ClubRepository>();
            services.AddRepositoryWithProxy<IEventsRepository, EventsRepository>();
            services.AddRepositoryWithProxy<IPaymentRepository, PaymentRepository>();
            services.AddRepositoryWithProxy<IClubReviewRepository, ClubReviewRepository>();
            services.AddRepositoryWithProxy<IDeviceRepository, DeviceRepository>();

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
            services.AddScoped<IFileUploadService, FileUploadService>();

            services.AddSingleton<ICustomLogger, FileLogger>();
            return services;
        }
    }
}
