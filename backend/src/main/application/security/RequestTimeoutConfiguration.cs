using Microsoft.AspNetCore.Http.Timeouts;

namespace backend.main.application.security
{
    public static class RequestTimeoutConfiguration
    {
        public const string LongRunning = "long";

        public static IServiceCollection AddCustomRequestTimeouts(this IServiceCollection services)
        {
            services.AddRequestTimeouts(options =>
            {
                options.DefaultPolicy = new RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(30),
                };
                options.AddPolicy(
                    LongRunning,
                    new RequestTimeoutPolicy { Timeout = TimeSpan.FromMinutes(5) }
                );
            });

            return services;
        }
    }
}
