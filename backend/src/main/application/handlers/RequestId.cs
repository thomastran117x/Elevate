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
        /// Whether a trace identifier is safe to copy into security events. The middleware only ever
        /// sets a generated GUID; this guards events recorded where it did not run, so nothing longer,
        /// or containing line breaks or markup, can forge or split log entries.
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
                // Always generated here. The id is the correlation id on every log line and security
                // event, so accepting one from the client would let a caller choose which requests
                // its own are joined with, including a victim's id copied from a response.
                var requestId = Guid.NewGuid().ToString("D");
                context.Request.Headers[RequestIdHeader] = requestId;
                context.TraceIdentifier = requestId;

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
