using System.Data.Common;

using backend.main.shared.exceptions.app;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace backend.main.infrastructure.database.repository
{
    public sealed class RepositoryResiliencePolicy : IRepositoryResiliencePolicy
    {
        private readonly AsyncPolicy _policy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
        private readonly ILogger<RepositoryResiliencePolicy>? _logger;

        public RepositoryResiliencePolicy(ILogger<RepositoryResiliencePolicy>? logger = null)
        {
            _logger = logger;

            var retryPolicy = Policy
                .Handle<Exception>(IsTransient)
                .WaitAndRetryAsync(
                    3,
                    attempt =>
                    {
                        double baseDelay = 100 * Math.Pow(2, attempt);
                        double jitter = 0.5 + Random.Shared.NextDouble();
                        return TimeSpan.FromMilliseconds(baseDelay * jitter);
                    },
                    onRetry: (ex, delay, attempt, _) =>
                    {
                        _logger?.LogWarning(
                            ex,
                            "Repository retry attempt {Attempt} after {DelayMs}ms for transient failure",
                            attempt,
                            delay.TotalMilliseconds
                        );
                    }
                );

            _circuitBreaker = Policy
                .Handle<Exception>(IsTransient)
                .CircuitBreakerAsync(
                    2,
                    TimeSpan.FromSeconds(10),
                    onBreak: (ex, _) =>
                    {
                        _logger?.LogWarning(
                            ex,
                            "Repository circuit breaker opened; database considered unavailable"
                        );
                    },
                    onReset: () =>
                    {
                        _logger?.LogInformation(
                            "Repository circuit breaker reset; database available again"
                        );
                    }
                );

            var timeoutPolicy = Policy.TimeoutAsync(
                TimeSpan.FromSeconds(3),
                TimeoutStrategy.Optimistic
            );

            _policy = Policy.WrapAsync(_circuitBreaker, retryPolicy, timeoutPolicy);
        }

        public bool IsDatabaseHealthy => _circuitBreaker.CircuitState == CircuitState.Closed;

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            string operationName,
            CancellationToken ct = default
        )
        {
            var context = new Context(operationName);
            try
            {
                return await _policy.ExecuteAsync((_, token) => action(token), context, ct);
            }
            catch (TimeoutRejectedException)
            {
                throw new RepositoryTimeoutException();
            }
            catch (BrokenCircuitException)
            {
                throw new RepositoryUnavailableException();
            }
            catch (DbUpdateException ex)
            {
                throw new RepositoryWriteException(ex.InnerException?.Message ?? ex.Message);
            }
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            string operationName,
            CancellationToken ct = default
        )
        {
            var context = new Context(operationName);
            try
            {
                await _policy.ExecuteAsync((_, token) => action(token), context, ct);
            }
            catch (TimeoutRejectedException)
            {
                throw new RepositoryTimeoutException();
            }
            catch (BrokenCircuitException)
            {
                throw new RepositoryUnavailableException();
            }
            catch (DbUpdateException ex)
            {
                throw new RepositoryWriteException(ex.InnerException?.Message ?? ex.Message);
            }
        }

        private static bool IsTransient(Exception ex)
        {
            if (ex is OperationCanceledException)
                return false;

            if (ex is TimeoutException)
                return true;

            if (ex is DbUpdateException { InnerException: DbException inner })
                return IsTransient(inner);

            if (ex is DbException dbEx)
                return IsTransientDbException(dbEx);

            return false;
        }

        private static bool IsTransientDbException(DbException ex)
        {
            string? sqlState = ex.SqlState;
            if (!string.IsNullOrEmpty(sqlState) && sqlState.Length >= 2)
            {
                string classCode = sqlState[..2];
                if (classCode is "23" or "27" or "42")
                    return false;
            }

            return true;
        }
    }
}