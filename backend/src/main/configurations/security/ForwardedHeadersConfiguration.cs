using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
namespace backend.main.configurations.security
{
    public static class ForwardedHeadersConfiguration
    {
        public static IServiceCollection AddForwardedHeaders(
            this IServiceCollection services,
            IConfiguration config
        )
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                options.RequireHeaderSymmetry = true;

                var proxyIps = config.GetSection("ForwardedHeaders:KnownProxies")
                    .Get<string[]>() ?? Array.Empty<string>();
                foreach (var proxyIp in proxyIps)
                {
                    if (IPAddress.TryParse(proxyIp, out var parsedProxy))
                        options.KnownProxies.Add(parsedProxy);
                }

                var networks = config.GetSection("ForwardedHeaders:KnownNetworks")
                    .Get<string[]>() ?? Array.Empty<string>();
                foreach (var network in networks)
                {
                    var parts = network.Split('/', StringSplitOptions.TrimEntries);
                    if (parts.Length != 2)
                        continue;

                    if (!IPAddress.TryParse(parts[0], out var prefix))
                        continue;

                    if (!int.TryParse(parts[1], out var prefixLength))
                        continue;

                    options.KnownNetworks.Add(
                        new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength)
                    );
                }
            });

            return services;
        }
    }
}
