using backend.main.application.environment;

namespace backend.main.configurations.security
{
    public static class SecurityHeadersConfiguration
    {
        private const string XContentTypeOptions = "X-Content-Type-Options";
        private const string XFrameOptions = "X-Frame-Options";
        private const string XPermittedCrossDomain = "X-Permitted-Cross-Domain-Policies";
        private const string ReferrerPolicy = "Referrer-Policy";
        private const string PermissionsPolicy = "Permissions-Policy";
        private const string ContentSecurityPolicy = "Content-Security-Policy";

        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                context.Response.Headers[XContentTypeOptions] = "nosniff";

                context.Response.Headers[XFrameOptions] = "DENY";

                context.Response.Headers[XPermittedCrossDomain] = "none";

                context.Response.Headers[ReferrerPolicy] = "strict-origin-when-cross-origin";

                context.Response.Headers[PermissionsPolicy] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

                context.Response.Headers[ContentSecurityPolicy] = "default-src 'none'; frame-ancestors 'none'";

                await next(context);
            });
        }

        public static IApplicationBuilder UseHttpsEnforcement(this IApplicationBuilder app)
        {
            var env = EnvironmentSetting.AppEnvironment;
            if (env is "development" or "test")
                return app;

            app.UseHsts();
            app.UseHttpsRedirection();
            return app;
        }
    }
}
