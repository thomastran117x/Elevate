using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Polly;
using Polly.CircuitBreaker;

namespace backend.main.infrastructure.elasticsearch
{
    public sealed class ElasticsearchCircuitBreaker
    {
        private readonly AsyncCircuitBreakerPolicy _policy;

        public ElasticsearchCircuitBreaker()
        {
            _policy = Policy
                .Handle<Exception>(ShouldHandle)
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 3,
                    durationOfBreak: TimeSpan.FromSeconds(20),
                    onBreak: (ex, delay) =>
                    {
                        Logger.Warn(
                            ex,
                            $"Elasticsearch circuit breaker opened for {delay.TotalSeconds:0}s.");
                    },
                    onReset: () =>
                    {
                        Logger.Info("Elasticsearch circuit breaker reset.");
                    },
                    onHalfOpen: () =>
                    {
                        Logger.Info("Elasticsearch circuit breaker is half-open; probing service health.");
                    });
        }

        public async Task ExecuteAsync(Func<Task> action, string operationName)
        {
            try
            {
                await _policy.ExecuteAsync(action);
            }
            catch (BrokenCircuitException ex)
            {
                throw new ElasticsearchUnavailableException(
                    $"Elasticsearch circuit breaker is open during {operationName}.",
                    ex);
            }
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string operationName)
        {
            try
            {
                return await _policy.ExecuteAsync(action);
            }
            catch (BrokenCircuitException ex)
            {
                throw new ElasticsearchUnavailableException(
                    $"Elasticsearch circuit breaker is open during {operationName}.",
                    ex);
            }
        }

        private static bool ShouldHandle(Exception ex) =>
            ex is not OperationCanceledException
            && ex is not ElasticsearchDisabledException
            && ex is not ElasticsearchConfigurationException;
    }
}
