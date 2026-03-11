namespace backend.main.configurations.application
{
    public static class RequestIdConfiguration
    {
        private const string RequestIdHeader = "X-Request-Id";

        public static IApplicationBuilder UseRequestId(this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequestIdMiddleware>();
        }

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
                    || string.IsNullOrWhiteSpace(existing)
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
