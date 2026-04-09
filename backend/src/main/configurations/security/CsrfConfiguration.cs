using Microsoft.AspNetCore.Antiforgery;

namespace backend.main.configurations.security
{
    public static class CsrfConfiguration
    {
        public const string CsrfHeaderName = "X-CSRF-TOKEN";
        public const string CsrfCookieName = "XSRF-TOKEN";
        private static readonly string[] ProtectedAuthPostPaths =
        [
            "/api/auth/login",
            "/api/auth/signup",
            "/api/auth/google",
            "/api/auth/microsoft",
            "/api/auth/refresh",
            "/api/auth/logout",
            "/api/auth/forgot-password",
            "/api/auth/change-password"
        ];

        public static IServiceCollection AddCustomCsrf(this IServiceCollection services)
        {
            services.AddAntiforgery(options =>
            {
                options.HeaderName = CsrfHeaderName;
                options.Cookie.Name = CsrfCookieName;

                // JS must read it and send it back in header
                options.Cookie.HttpOnly = false;

                options.Cookie.SameSite = SameSiteMode.Lax;
                // SameAsRequest: secure over HTTPS (production), works over HTTP (local dev)
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

            return services;
        }
        public static void SetCsrfCookie(HttpContext httpContext, IAntiforgery antiforgery)
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);
            if (string.IsNullOrWhiteSpace(tokens.RequestToken))
                return;

            httpContext.Response.Cookies.Append(
                CsrfCookieName,
                tokens.RequestToken,
                new CookieOptions
                {
                    HttpOnly = false,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });
        }

        public static IApplicationBuilder UseRefreshCsrfCookie(this IApplicationBuilder app)
        {
            return app.Use(async (ctx, next) =>
            {
                if (IsRefreshEndpoint(ctx.Request))
                {
                    var antiforgery = ctx.RequestServices.GetRequiredService<IAntiforgery>();
                    SetCsrfCookie(ctx, antiforgery);
                }

                await next();
            });
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

        private static bool IsRefreshEndpoint(HttpRequest request)
        {
            return request.Path.StartsWithSegments("/api/auth/refresh", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCsrfProtectedEndpoint(HttpRequest request)
        {
            return ProtectedAuthPostPaths.Any(path =>
                request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
