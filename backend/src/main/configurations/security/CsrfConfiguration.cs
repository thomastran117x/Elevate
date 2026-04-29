using backend.main.configurations.application;
using Microsoft.AspNetCore.Antiforgery;

namespace backend.main.configurations.security
{
    public static class CsrfConfiguration
    {
        public const string CsrfHeaderName = "X-CSRF-TOKEN";
        public const string CsrfCookieName = "XSRF-TOKEN";
        private static readonly string[] ProtectedAuthPostPaths =
        [
            $"{RoutePaths.ApiAuthPath}/login",
            $"{RoutePaths.ApiAuthPath}/signup",
            $"{RoutePaths.ApiAuthPath}/verify/otp",
            $"{RoutePaths.ApiAuthPath}/google",
            $"{RoutePaths.ApiAuthPath}/microsoft",
            $"{RoutePaths.ApiAuthPath}/refresh",
            $"{RoutePaths.ApiAuthPath}/logout",
            $"{RoutePaths.ApiAuthPath}/forgot-password",
            $"{RoutePaths.ApiAuthPath}/change-password"
        ];

        public static IServiceCollection AddCustomCsrf(this IServiceCollection services)
        {
            services.AddAntiforgery(options =>
            {
                options.HeaderName = CsrfHeaderName;
                options.Cookie.Name = CsrfCookieName;

                // JS must read it and send it back in header
                options.Cookie.HttpOnly = false;

                options.Cookie.SameSite = SameSiteMode.Strict;
                // SameAsRequest: secure over HTTPS (production), works over HTTP (local dev)
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

            return services;
        }
        public static IApplicationBuilder UseRefreshCsrfValidation(this IApplicationBuilder app)
        {
            return app.Use(async (ctx, next) =>
            {
                if (IsCsrfProtectedEndpoint(ctx.Request) && HttpMethods.IsPost(ctx.Request.Method))
                {
                    var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
                    await antiforgery.ValidateRequestAsync(ctx);
                }

                await next();
            });
        }

        private static bool IsCsrfProtectedEndpoint(HttpRequest request)
        {
            return ProtectedAuthPostPaths.Any(path =>
                request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
