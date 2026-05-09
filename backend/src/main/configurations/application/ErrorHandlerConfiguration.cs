using backend.main.utilities.implementation;
using backend.main.dtos.responses.general;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.configurations.application
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;

        public GlobalExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (AntiforgeryValidationException ex)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object?>.Failure(
                        "CSRF validation failed.",
                        "CSRF_VALIDATION_FAILED",
                        new
                        {
                            reason = ex.Message
                        }
                    )
                );
            }
            catch (Exception ex)
            {
                var result = HandleError.Resolve(ex) as ObjectResult;

                context.Response.StatusCode = result?.StatusCode ?? 500;
                context.Response.ContentType = "application/json";

                if (context.Response.StatusCode >= 500)
                {
                    Logger.Error("There was a critical server error. Please investigate");
                    Logger.Error(ex);
                }

                await context.Response.WriteAsJsonAsync(result?.Value);
            }
        }
    }
}
