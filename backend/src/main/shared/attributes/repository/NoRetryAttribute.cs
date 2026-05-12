namespace backend.main.shared.attributes.repository
{
    /// When applied to a repository method, the call runs once with no retries, circuit breaker, or timeout.
    ///
    /// Use for:
    ///   - Non-idempotent writes (retrying could cause duplicate side effects)
    ///   - Operations already wrapped in application-level retry or saga logic
    ///   - One-off or diagnostic calls where retry is not desired
    ///
    /// If neither NoRetry nor RetryOnTransientFailure is present on a method, the proxy defaults to retry with backoff.
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
    public sealed class NoRetryAttribute : Attribute;
}
