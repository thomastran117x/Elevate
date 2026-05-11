using System.Net;

using backend.main.shared.http;

using Microsoft.Extensions.Http.Resilience;

using Polly;

namespace backend.main.application.handlers
{
    public static class WebHandler
    {
        private const string ResiliencePipeline = "external-api-pipeline";

        public static IServiceCollection AddWebConfiguration(
            this IServiceCollection services,
            IConfiguration config
        )
        {
            services.AddExternalApiClient(config);
            return services;
        }

        private static void AddExternalApiClient(
            this IServiceCollection services,
            IConfiguration config
        )
        {
            var timeoutSeconds = config.GetValue<int?>("ExternalApi:TimeoutSeconds") ?? 10;

            var maxRetries = config.GetValue<int?>("ExternalApi:Retry:MaxAttempts") ?? 3;

            var baseDelayMs = config.GetValue<int?>("ExternalApi:Retry:BaseDelayMs") ?? 200;

            services
                .AddHttpClient<IExternalApiClient, ExternalApiClient>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                })
                .AddResilienceHandler(
                    ResiliencePipeline,
                    (builder, context) =>
                    {
                        var loggerFactory =
                            context.ServiceProvider.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger("ExternalApiClient");

                        builder.AddRetry(
                            new HttpRetryStrategyOptions
                            {
                                MaxRetryAttempts = maxRetries,
                                Delay = TimeSpan.FromMilliseconds(baseDelayMs),
                                BackoffType = DelayBackoffType.Exponential,
                                UseJitter = true,

                                ShouldHandle = static args =>
                                {
                                    var outcome = args.Outcome;

                                    if (
                                        outcome.Exception
                                        is HttpRequestException
                                            or TaskCanceledException
                                    )
                                        return ValueTask.FromResult(true);

                                    var resp = outcome.Result;
                                    if (resp is null)
                                        return ValueTask.FromResult(false);

                                    if ((int)resp.StatusCode >= 500)
                                        return ValueTask.FromResult(true);
                                    if (resp.StatusCode == HttpStatusCode.RequestTimeout)
                                        return ValueTask.FromResult(true);
                                    if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                                        return ValueTask.FromResult(true);

                                    return ValueTask.FromResult(false);
                                },

                                OnRetry = args =>
                                {
                                    var reason =
                                        args.Outcome.Exception?.Message
                                        ?? (
                                            args.Outcome.Result is not null
                                                ? $"HTTP {(int)args.Outcome.Result.StatusCode}"
                                                : "Unknown"
                                        );

                                    logger.LogWarning(
                                        "[ExternalApi] Retry {Attempt} after {Delay}ms — {Reason}",
                                        args.AttemptNumber,
                                        (int)args.RetryDelay.TotalMilliseconds,
                                        reason
                                    );

                                    return ValueTask.CompletedTask;
                                },
                            }
                        );
                    }
                );
        }
    }
}
