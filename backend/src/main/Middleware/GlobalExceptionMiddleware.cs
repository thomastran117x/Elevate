using backend.main.Utilities;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

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

            await context.Response.WriteAsJsonAsync(new
            {
                message = "CSRF validation failed.",
                detail = ex.Message,
            });
        }
        catch (Exception ex)
        {
            var result = ErrorUtility.HandleError(ex) as ObjectResult;

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
