using System.Net;

using backend.main.services.implementations;
using backend.main.services.interfaces;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;

using Polly;

namespace backend.main.configurations.application
{
    public static class WebConfiguration
    {
        private const string CaptchaPipelineName = "captcha-pipeline";

        public static IServiceCollection AddWebConfiguration(
            this IServiceCollection services,
            IConfiguration config)
        {
            services.AddCaptchaClient(config);
            return services;
        }

        private static IServiceCollection AddCaptchaClient(
            this IServiceCollection services,
            IConfiguration config)
        {
            var captchaTimeoutSeconds =
                config.GetValue<int?>("GoogleCaptcha:TimeoutSeconds") ?? 5;

            var retries =
                config.GetValue<int?>("GoogleCaptcha:Retry:Count") ?? 2;

            var retryBaseDelayMs =
                config.GetValue<int?>("GoogleCaptcha:Retry:BaseDelayMs") ?? 200;

            var failuresBeforeBreak =
                config.GetValue<int?>("GoogleCaptcha:Breaker:FailuresBeforeBreak") ?? 5;

            var breakSeconds =
                config.GetValue<int?>("GoogleCaptcha:Breaker:BreakSeconds") ?? 45;

            services
                .AddHttpClient<ICaptchaService, GoogleCaptchaService>(client =>
                {
                    client.BaseAddress = new Uri("https://www.google.com/");
                    client.Timeout = TimeSpan.FromSeconds(captchaTimeoutSeconds);
                })
                .AddResilienceHandler(CaptchaPipelineName, (builder, context) =>
                {
                    var loggerFactory = context.ServiceProvider.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("CaptchaHttpClient");

                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = retries,
                        Delay = TimeSpan.FromMilliseconds(retryBaseDelayMs),
                        BackoffType = DelayBackoffType.Linear,
                        UseJitter = true,

                        ShouldHandle = static args =>
                        {
                            var outcome = args.Outcome;

                            if (outcome.Exception is HttpRequestException)
                                return ValueTask.FromResult(true);

                            if (outcome.Exception is TaskCanceledException)
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
                                args.Outcome.Exception?.Message ??
                                (args.Outcome.Result is not null ? $"HTTP {(int)args.Outcome.Result.StatusCode}" : "Unknown");

                            logger.LogWarning(
                                "[Captcha] Retry {Attempt} after {Delay}ms due to {Reason}",
                                args.AttemptNumber,
                                (int)args.RetryDelay.TotalMilliseconds,
                                reason
                            );

                            return ValueTask.CompletedTask;
                        }
                    });

                    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 1.0,
                        MinimumThroughput = failuresBeforeBreak,
                        SamplingDuration = TimeSpan.FromSeconds(30),

                        BreakDuration = TimeSpan.FromSeconds(breakSeconds),

                        ShouldHandle = static args =>
                        {
                            var outcome = args.Outcome;

                            if (outcome.Exception is HttpRequestException)
                                return ValueTask.FromResult(true);

                            if (outcome.Exception is TaskCanceledException)
                                return ValueTask.FromResult(true);

                            var resp = outcome.Result;
                            if (resp is null)
                                return ValueTask.FromResult(false);

                            if ((int)resp.StatusCode >= 500)
                                return ValueTask.FromResult(true);
                            if (resp.StatusCode == HttpStatusCode.RequestTimeout)
                                return ValueTask.FromResult(true);

                            return ValueTask.FromResult(false);
                        },

                        OnOpened = args =>
                        {
                            var reason =
                                args.Outcome.Exception?.Message ??
                                (args.Outcome.Result is not null ? $"HTTP {(int)args.Outcome.Result.StatusCode}" : "Unknown");

                            logger.LogError(
                                "[Captcha] Circuit OPEN for {BreakSeconds}s. Last failure: {Reason}",
                                (int)args.BreakDuration.TotalSeconds,
                                reason
                            );

                            return ValueTask.CompletedTask;
                        },

                        OnClosed = _ =>
                        {
                            logger.LogInformation("[Captcha] Circuit RESET. Resuming calls.");
                            return ValueTask.CompletedTask;
                        },

                        OnHalfOpened = _ =>
                        {
                            logger.LogInformation("[Captcha] Circuit HALF-OPEN. Testing a request.");
                            return ValueTask.CompletedTask;
                        }
                    });
                });

            return services;
        }
    }
}
