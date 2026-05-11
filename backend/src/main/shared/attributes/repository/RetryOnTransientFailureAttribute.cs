namespace backend.main.shared.attributes.repository
{
    /// Marks a repository method (or class/interface) to run with retry and exponential backoff on transient failures.
    ///
    /// Methods are executed through the shared resilience policy:
    ///   - Up to 3 retries with exponential backoff and jitter
    ///   - Circuit breaker after 2 failures (open 10s)
    ///   - 3s timeout per attempt
    ///
    /// This is the default for repository interface methods. Apply explicitly to document intent or when
    /// mixing with NoRetry on other methods. Can be applied at interface, class, or method level.
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RetryOnTransientFailureAttribute : Attribute;
}
