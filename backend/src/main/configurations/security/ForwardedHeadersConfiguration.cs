using Microsoft.AspNetCore.HttpOverrides;

namespace backend.main.configurations.security
{
    public static class ForwardedHeadersConfiguration
    {
        public static IServiceCollection AddForwardedHeaders(this IServiceCollection services)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            return services;
        }
    }
}
