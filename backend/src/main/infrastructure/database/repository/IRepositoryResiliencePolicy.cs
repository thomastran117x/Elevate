namespace backend.main.infrastructure.database.repository
{
    public interface IRepositoryResiliencePolicy
    {
        Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            string operationName,
            CancellationToken ct = default
        );
        Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            string operationName,
            CancellationToken ct = default
        );
        bool IsDatabaseHealthy { get; }
    }
}
