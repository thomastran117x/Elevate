using System.Data.Common;

using backend.main.Resources;

using Microsoft.EntityFrameworkCore;

using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace backend.main.Repositories
{
    public sealed class ExecuteOptions
    {
        public string OperationName { get; init; } = "RepositoryOperation";
        public bool AllowRetry { get; init; } = true;

        public static ExecuteOptions Default(string? name = null) =>
            new()
            {
                OperationName = name ?? "RepositoryOperation",
                AllowRetry = true
            };

        public static ExecuteOptions NoRetry(string? name = null) =>
            new()
            {
                OperationName = name ?? "RepositoryOperation",
                AllowRetry = false
            };
    }

    public abstract class BaseRepository
    {
        protected readonly AppDatabaseContext _context;

        private readonly AsyncPolicy _policy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
        private static readonly ThreadLocal<Random> Jitterer =
            new(() => new Random());

        protected BaseRepository(AppDatabaseContext context)
        {
            _context = context;

            var retryPolicy = Policy
                .Handle<Exception>(IsTransient)
                .WaitAndRetryAsync(
                    3,
                    attempt =>
                    {
                        double baseDelay = 100 * Math.Pow(2, attempt);
                        double jitter = 0.5 + Jitterer.Value!.NextDouble();
                        return TimeSpan.FromMilliseconds(baseDelay * jitter);
                    });

            _circuitBreaker = Policy
                .Handle<Exception>(IsTransient)
                .CircuitBreakerAsync(
                    2,
                    TimeSpan.FromSeconds(10));

            var timeoutPolicy = Policy
                .TimeoutAsync(
                    TimeSpan.FromSeconds(3),
                    TimeoutStrategy.Optimistic);

            _policy = Policy.WrapAsync(
                _circuitBreaker,
                retryPolicy,
                timeoutPolicy
            );
        }

        protected Task<T> ExecuteAsync<T>(Func<Task<T>> action)
            => ExecuteAsync(
                _ => action(),
                ExecuteOptions.Default(),
                CancellationToken.None);

        protected Task ExecuteAsync(Func<Task> action)
            => ExecuteAsync(
                _ => action(),
                ExecuteOptions.Default(),
                CancellationToken.None);

        protected Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            ExecuteOptions? options,
            CancellationToken ct = default)
        {
            options ??= ExecuteOptions.Default();

            if (!options.AllowRetry)
                return action(ct);

            var context = new Context(options.OperationName);

            return _policy.ExecuteAsync(
                async (_, token) => await action(token),
                context,
                ct);
        }

        protected Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            ExecuteOptions? options,
            CancellationToken ct = default)
        {
            options ??= ExecuteOptions.Default();

            if (!options.AllowRetry)
                return action(ct);

            var context = new Context(options.OperationName);

            return _policy.ExecuteAsync(
                async (_, token) => await action(token),
                context,
                ct);
        }

        protected static bool IsTransient(Exception ex)
        {
            if (ex is TimeoutException)
                return true;

            if (ex is DbUpdateException { InnerException: DbException inner })
                return IsTransient(inner);

            if (ex is DbException)
                return true;

            return false;
        }

        public bool IsDatabaseHealthy =>
            _circuitBreaker.CircuitState == CircuitState.Closed;
    }
}
