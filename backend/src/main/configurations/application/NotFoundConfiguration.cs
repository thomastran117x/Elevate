namespace backend.main.configurations.application
{
    public static class NotFoundConfig
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

                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Resource not found",
                        code = 404,
                        path = context.Request.Path.Value
                    });
                }
            });
        }
    }
}
