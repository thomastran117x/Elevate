namespace backend.main.application.security
{
    public static class CorsConfiguration
    {
        public const string DefaultPolicyName = "DefaultCors";

        public static IServiceCollection AddCustomCors(
            this IServiceCollection services,
            IConfiguration config
        )
        {
            var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();

            services.AddCors(options =>
            {
                options.AddPolicy(DefaultPolicyName, builder =>
                {
                    if (origins.Length == 0)
                    {
                        builder
                            .WithOrigins("http://localhost:3090")
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    }
                    else
                    {
                        builder
                            .WithOrigins(origins)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    }

                    builder.WithExposedHeaders(
                        "X-Request-Id",
                        "X-RateLimit-Limit",
                        "X-RateLimit-Remaining",
                        "X-RateLimit-Reset"
                    );
                });
            });

            return services;
        }
    }
}
