using backend.main.shared.requests;

namespace backend.main.application.handlers
{
    public static class ClientInfoHandler
    {
        public static IServiceCollection AddClientRequestInspection(
            this IServiceCollection services
        )
        {
            services.AddScoped<ClientRequestInfo>();
            return services;
        }

        public static IApplicationBuilder UseClientRequestInspection(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ClientRequestInspectionMiddleware>();
        }
    }

    public class ClientRequestInspectionMiddleware
    {
        private readonly RequestDelegate _next;

        public ClientRequestInspectionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ClientRequestInfo requestInfo)
        {
            requestInfo.IpAddress = ResolveIpAddress(context);
            requestInfo.ClientName = ResolveClientName(context);
            requestInfo.DeviceType = ResolveDeviceType(context);
            requestInfo.IsBrowserClient = ResolveIsBrowser(context);

            await _next(context);
        }

        private static bool ResolveIsBrowser(HttpContext context)
        {
            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
                return true;

            return !(
                userAgent.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("curl/", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("axios/", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("HttpClient", StringComparison.OrdinalIgnoreCase)
            );
        }

        private static string ResolveIpAddress(HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        private static string ResolveClientName(HttpContext context)
        {
            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Unknown";

            if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
                return "Edge";
            if (
                userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Opera", StringComparison.OrdinalIgnoreCase)
            )
                return "Opera";
            if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
                return "Chrome";
            if (
                userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)
                && !userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase)
            )
                return "Safari";
            if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
                return "Firefox";

            if (userAgent.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase))
                return "Postman";
            if (userAgent.Contains("curl/", StringComparison.OrdinalIgnoreCase))
                return "cURL";
            if (userAgent.Contains("axios/", StringComparison.OrdinalIgnoreCase))
                return "Axios";
            if (userAgent.Contains("HttpClient", StringComparison.OrdinalIgnoreCase))
                return "HttpClient";

            return userAgent.Split('/')[0].Trim();
        }

        private static string ResolveDeviceType(HttpContext context)
        {
            var userAgent = context.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
                return "Unknown";

            if (
                userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
                    && !userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
            )
                return "Mobile";

            if (
                userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            )
                return "Tablet";

            if (
                userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)
                    && !userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
            )
                return "Desktop";

            if (
                userAgent.Contains("PostmanRuntime", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("curl/", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("HttpClient", StringComparison.OrdinalIgnoreCase)
                || userAgent.Contains("axios/", StringComparison.OrdinalIgnoreCase)
            )
                return "API Client";

            return "Unknown";
        }
    }
}

