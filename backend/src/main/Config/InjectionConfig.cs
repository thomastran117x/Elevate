using backend.main.Interfaces;
using backend.main.Queues;
using backend.main.Repositories;
using backend.main.Resources;
using backend.main.Services;
using backend.main.Utilities;

namespace backend.main.Config
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IFollowRepository, FollowRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IClubRepository, ClubRepository>();
            services.AddScoped<IEventsRepository, EventsRepository>();

            services.AddSingleton<IPublisher, Publisher>();

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IOAuthService, OAuthService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IClubService, ClubService>();
            services.AddScoped<IFollowService, FollowService>();
            services.AddScoped<IEventsService, EventsService>();
            services.AddScoped<IFileUploadService, FileUploadService>();

            services.AddSingleton<ICacheService, CacheService>();
            return services;
        }
    }
}
