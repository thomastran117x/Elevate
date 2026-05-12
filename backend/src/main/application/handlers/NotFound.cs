using backend.main.shared.responses;

namespace backend.main.application.handlers
{
    public static class NotFoundHandler
    {
        public static IApplicationBuilder UseJsonNotFound(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                await next();

                if (context.Response.HasStarted)
                    return;

                if (context.Response.StatusCode == StatusCodes.Status404NotFound)
                {
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsJsonAsync(
                        ApiResponse<object?>.Failure(
                            "Resource not found.",
                            "RESOURCE_NOT_FOUND",
                            new
                            {
                                path = context.Request.Path.Value
                            }
                        )
                    );
                }
            });
        }
    }
}

