using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.application.handlers
{
    public class GlobalExceptionHandler
    {
        /// <summary>nginx's "client closed request"; not a member of <see cref="StatusCodes"/>.</summary>
        private const int ClientClosedRequestStatusCode = 499;

        private readonly RequestDelegate _next;

        public GlobalExceptionHandler(RequestDelegate next)
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
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away. Nothing can be written to a closed connection, and this is
                // not a fault: reporting it as 500 would log a critical server error for every
                // abandoned upload or navigation. 499 is nginx's "client closed request", recorded
                // for the access log only.
                //
                // The filter matters. The request-timeout middleware runs inside this one and
                // cancels through its own linked token, catching the result itself to write 504,
                // so a server timeout never reaches here. Only a genuine disconnect does.
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = ClientClosedRequestStatusCode;
            }
            catch (OperationCanceledException ex)
            {
                // Cancelled, but not by the client: an internal token, or a timeout whose
                // middleware could not answer. Nothing sensible to return, and HandleError
                // deliberately rethrows this type rather than resolving it, so the envelope is
                // built here instead of calling it.
                Logger.Error("A request was cancelled without the client disconnecting.");
                Logger.Error(ex);

                if (context.Response.HasStarted)
                    return;

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object?>.Failure("An unexpected error occurred.", "INTERNAL_SERVER_ERROR"));
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

