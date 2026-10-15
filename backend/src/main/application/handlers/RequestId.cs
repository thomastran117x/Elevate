using System.Text.RegularExpressions;

namespace backend.main.application.handlers
{
    public static partial class RequestIdHandler
    {
        private const string RequestIdHeader = "X-Request-Id";

        public static IApplicationBuilder UseRequestId(this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequestIdMiddleware>();
        }

        /// <summary>
        /// Whether a request id is safe to log and echo. The id becomes the trace identifier that
        /// security events and logs carry, so a client-supplied value is accepted only in this
        /// narrow form: anything longer, or containing line breaks or markup, could forge or split
        /// log entries.
        /// </summary>
        internal static bool IsValidRequestId(string? value) =>
            !string.IsNullOrEmpty(value) && RequestIdPattern().IsMatch(value);

        // \z, not $: $ also matches before a trailing newline, which would let "id\n" through.
        [GeneratedRegex(@"\A[A-Za-z0-9._:-]{1,128}\z")]
        private static partial Regex RequestIdPattern();

        private class RequestIdMiddleware
        {
            private readonly RequestDelegate _next;

            public RequestIdMiddleware(RequestDelegate next)
            {
                _next = next;
            }

            public async Task InvokeAsync(HttpContext context)
            {
                if (
                    !context.Request.Headers.TryGetValue(RequestIdHeader, out var existing)
                    || !IsValidRequestId(existing.ToString())
                )
                {
                    existing = Guid.NewGuid().ToString("D");
                    context.Request.Headers[RequestIdHeader] = existing;
                }

                context.TraceIdentifier = existing!;

                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[RequestIdHeader] = context.TraceIdentifier;
                    return Task.CompletedTask;
                });

                await _next(context);
            }
        }
    }
}
