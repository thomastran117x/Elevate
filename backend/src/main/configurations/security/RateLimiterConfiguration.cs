using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace backend.main.configurations.security
{
    public static class RateLimiterConfiguration
    {
        private const int PermitLimit = 100;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        public static IServiceCollection AddInMemoryRateLimiter(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        status = 429,
                        error = "Too Many Requests",
                        message = "Rate limit exceeded. Please try again later."
                    }, cancellationToken);
                };

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                {
                    try
                    {
                        string partitionKey = GetPartitionKey(context);
                        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PermitLimit,
                            Window = Window,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        });
                    }
                    catch
                    {
                        return RateLimitPartition.GetFixedWindowLimiter("fail-closed", _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 0,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        });
                    }
                });
            });

            return services;
        }

        private static string GetPartitionKey(HttpContext context)
        {
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                    return $"user:{userId}";
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return $"ip:{ip}";
        }
    }
}
