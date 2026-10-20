using System.Threading.RateLimiting;

namespace backend.main.application.security
{
    /// <summary>
    /// Wraps the global rate limiter and marks each request it refuses.
    /// </summary>
    /// <remarks>
    /// The rate-limiting middleware reports a refusal by the global limiter and by an endpoint
    /// policy through the same <c>OnRejected</c> callback, and the endpoint metadata names only the
    /// policy. Without this mark, exhausting the global budget on an auth route would read as an
    /// authentication throttle. The middleware tries the global limiter first and stops when it
    /// refuses, so a marked request was never seen by the endpoint policy.
    /// </remarks>
    internal sealed class GlobalLimiterRejectionMarker : PartitionedRateLimiter<HttpContext>
    {
        private static readonly object RejectedKey = new();

        private readonly PartitionedRateLimiter<HttpContext> _inner;

        public GlobalLimiterRejectionMarker(PartitionedRateLimiter<HttpContext> inner)
        {
            _inner = inner;
        }

        /// <summary>Whether the global limiter, rather than an endpoint policy, refused the request.</summary>
        public static bool WasRejected(HttpContext context) => context.Items.ContainsKey(RejectedKey);

        public override RateLimiterStatistics? GetStatistics(HttpContext resource) =>
            _inner.GetStatistics(resource);

        protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount) =>
            Mark(resource, _inner.AttemptAcquire(resource, permitCount));

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
            HttpContext resource,
            int permitCount,
            CancellationToken cancellationToken) =>
            Mark(resource, await _inner.AcquireAsync(resource, permitCount, cancellationToken));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }

        protected override async ValueTask DisposeAsyncCore()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsyncCore();
        }

        private static RateLimitLease Mark(HttpContext resource, RateLimitLease lease)
        {
            // A failed attempt can be followed by a successful wait, so the mark follows the latest result.
            if (lease.IsAcquired)
                resource.Items.Remove(RejectedKey);
            else
                resource.Items[RejectedKey] = true;

            return lease;
        }
    }
}
