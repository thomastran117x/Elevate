using System.Net.Http.Json;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace backend.main.shared.http
{
    public abstract class BaseService
    {
        protected readonly HttpClient Http;
        protected readonly AsyncRetryPolicy RetryPolicy;
        protected readonly AsyncCircuitBreakerPolicy CircuitBreakerPolicy;

        private static readonly Random Jitter = new();

        protected BaseService(HttpClient? httpClient = null)
        {
            Http = httpClient ?? new HttpClient();

            RetryPolicy = Policy
                .Handle<Exception>(IsTransient)
                .WaitAndRetryAsync(
                    retryCount: 5,
                    sleepDurationProvider: attempt =>
                    {
                        var baseDelay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt));
                        var jitter = TimeSpan.FromMilliseconds(Jitter.Next(0, 200));
                        return baseDelay + jitter;
                    }
                );

            CircuitBreakerPolicy = Policy
                .Handle<Exception>(IsTransient)
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(10)
                );
        }

        /// <summary>
        /// Whether a failure is worth retrying: a network fault or timeout. A deterministic
        /// rejection, such as an invalid token, fails the same way every time, so retrying it only
        /// holds the request open and lets one bad caller open the circuit.
        /// </summary>
        internal static bool IsTransient(Exception exception) => exception switch
        {
            HttpRequestException or TimeoutException or IOException => true,
            TaskCanceledException { InnerException: TimeoutException } => true,
            _ => false,
        };

        protected async Task<T> ExecuteResilientHttpAsync<T>(Func<Task<T>> action)
        {
            return await RetryPolicy
                .WrapAsync(CircuitBreakerPolicy)
                .ExecuteAsync(action);
        }
    }
}
