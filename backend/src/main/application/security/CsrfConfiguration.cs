using backend.main.application.bootstrap;

using Microsoft.AspNetCore.Antiforgery;

namespace backend.main.application.security
{
    public static class CsrfConfiguration
    {
        public const string CsrfHeaderName = "X-CSRF-TOKEN";
        public const string CsrfCookieName = "XSRF-TOKEN";
        private static readonly HashSet<string> ProtectedAuthPostPathSet =
        [
            $"{RoutePaths.ApiAuthPath}/login",
            $"{RoutePaths.ApiAuthPath}/signup",
            $"{RoutePaths.ApiAuthPath}/verify",
            $"{RoutePaths.ApiAuthPath}/verify/otp",
            $"{RoutePaths.ApiAuthPath}/google",
            $"{RoutePaths.ApiAuthPath}/google/code",
            $"{RoutePaths.ApiAuthPath}/microsoft",
            $"{RoutePaths.ApiAuthPath}/oauth/complete",
            $"{RoutePaths.ApiAuthPath}/mfa/start",
            $"{RoutePaths.ApiAuthPath}/mfa/verify",
            $"{RoutePaths.ApiAuthPath}/mfa/verify/totp",
            $"{RoutePaths.ApiAuthPath}/refresh",
            $"{RoutePaths.ApiAuthPath}/logout",
            $"{RoutePaths.ApiAuthPath}/forgot-password",
            $"{RoutePaths.ApiAuthPath}/change-password",
            $"{RoutePaths.ApiAuthPath}/mfa/enroll/start",
            $"{RoutePaths.ApiAuthPath}/mfa/enroll/verify",
            $"{RoutePaths.ApiAuthPath}/mfa/enable/start",
            $"{RoutePaths.ApiAuthPath}/mfa/disable",
            $"{RoutePaths.ApiAuthPath}/mfa/remove",
            $"{RoutePaths.ApiAuthPath}/mfa/sms/enroll/start",
            $"{RoutePaths.ApiAuthPath}/mfa/sms/enroll/verify",
            $"{RoutePaths.ApiAuthPath}/mfa/sms/enable/start",
            $"{RoutePaths.ApiAuthPath}/mfa/sms/disable",
            $"{RoutePaths.ApiAuthPath}/mfa/sms/remove",
            $"{RoutePaths.ApiAuthPath}/mfa/totp/enroll/start",
            $"{RoutePaths.ApiAuthPath}/mfa/totp/enroll/verify",
            $"{RoutePaths.ApiAuthPath}/mfa/totp/enable",
            $"{RoutePaths.ApiAuthPath}/mfa/totp/disable",
            $"{RoutePaths.ApiAuthPath}/mfa/totp/remove"
        ];
        public static IReadOnlySet<string> ProtectedAuthPostPaths => ProtectedAuthPostPathSet;

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
            return ProtectedAuthPostPathSet.Any(path =>
                request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
