using backend.main.features.auth.events;

namespace backend.main.application.security
{
    /// <summary>
    /// Marks an authentication endpoint to be timed into <c>auth.operation.duration</c>, tagged
    /// with the operation and whether it succeeded, was refused, was throttled, or faulted.
    /// </summary>
    /// <remarks>
    /// The attribute is endpoint metadata; <see cref="AuthOperationMetricsMiddleware"/> does the
    /// timing. It runs before the rate limiter, authentication, and authorization, so their
    /// short-circuited 429, 401, and 403 responses are counted along with the action's own.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class AuthOperationAttribute : Attribute
    {
        public AuthOperationAttribute(AuthOperation operation)
        {
            Operation = operation;
        }

        public AuthOperation Operation
        {
            get;
        }

        /// <summary>
        /// Classifies the final status code, so an expected refusal (a wrong password, an expired
        /// code) is told apart from a service fault on a dashboard without the response telling the
        /// client anything new.
        /// </summary>
        internal static AuthSecurityOutcome Classify(int statusCode) => statusCode switch
        {
            >= 500 => AuthSecurityOutcome.Faulted,
            StatusCodes.Status429TooManyRequests => AuthSecurityOutcome.Throttled,
            >= 400 => AuthSecurityOutcome.Denied,
            _ => AuthSecurityOutcome.Succeeded,
        };
    }

    public static class AuthOperationMetricsHandler
    {
        /// <summary>Mount after routing, so the endpoint and its metadata are known.</summary>
        public static IApplicationBuilder UseAuthOperationMetrics(this IApplicationBuilder app)
        {
            return app.UseMiddleware<AuthOperationMetricsMiddleware>();
        }
    }

    /// <summary>
    /// Times requests to endpoints marked <see cref="AuthOperationAttribute"/>. A request the client
    /// abandoned is not recorded.
    /// </summary>
    public sealed class AuthOperationMetricsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AuthSecurityMetrics _metrics;
        private readonly TimeProvider _timeProvider;

        public AuthOperationMetricsMiddleware(
            RequestDelegate next,
            AuthSecurityMetrics metrics,
            TimeProvider timeProvider)
        {
            _next = next;
            _metrics = metrics;
            _timeProvider = timeProvider;
        }

        public Task InvokeAsync(HttpContext context)
        {
            var operation = context.GetEndpoint()?.Metadata.GetMetadata<AuthOperationAttribute>();
            if (operation is null)
                return _next(context);

            var started = _timeProvider.GetTimestamp();

            // Recorded once the response has completed, from the status the client actually
            // received. An exception that escapes this middleware is answered further out (the
            // global handler turns a CSRF failure into 400 and an AppException into its own code),
            // so guessing the status here would misclassify those responses.
            context.Response.OnCompleted(() =>
            {
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    _metrics.RecordOperation(
                        operation.Operation,
                        AuthOperationAttribute.Classify(context.Response.StatusCode),
                        _timeProvider.GetElapsedTime(started));
                }

                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
