using backend.main.shared.responses;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace backend.main.application.handlers
{
    /// <summary>
    /// Replaces the default <see cref="ProblemDetailsClientErrorFactory"/> so framework-generated
    /// client errors (e.g. antiforgery/CSRF filter failures, bodyless 4xx results that never reach a
    /// controller action) are returned in the app's <see cref="ApiResponse{T}"/> envelope with a
    /// meaningful message — instead of a bare RFC ProblemDetails the frontend cannot read.
    /// </summary>
    public class ApiClientErrorFactory : IClientErrorFactory
    {
        public IActionResult? GetClientError(ActionContext actionContext, IClientErrorActionResult clientError)
        {
            var statusCode = clientError.StatusCode ?? StatusCodes.Status500InternalServerError;

            // The [ValidateAntiForgeryToken] filter short-circuits with an
            // AntiforgeryValidationFailedResult on CSRF failure (matched by type name to avoid
            // depending on the internal MVC assembly that declares the marker interface).
            if (clientError.GetType().Name == "AntiforgeryValidationFailedResult")
            {
                return BuildResponse(
                    StatusCodes.Status400BadRequest,
                    "CSRF validation failed. Refresh the page and try again.",
                    "CSRF_VALIDATION_FAILED"
                );
            }

            var (message, code) = statusCode switch
            {
                StatusCodes.Status400BadRequest => ("The request was invalid.", "BAD_REQUEST"),
                StatusCodes.Status401Unauthorized => ("Authentication is required.", "UNAUTHORIZED"),
                StatusCodes.Status403Forbidden => ("You do not have permission to perform this action.", "FORBIDDEN"),
                StatusCodes.Status404NotFound => ("The requested resource was not found.", "NOT_FOUND"),
                StatusCodes.Status405MethodNotAllowed => ("This action is not allowed.", "METHOD_NOT_ALLOWED"),
                StatusCodes.Status415UnsupportedMediaType => ("The request format is not supported.", "UNSUPPORTED_MEDIA_TYPE"),
                _ => ("The request could not be completed.", "REQUEST_ERROR"),
            };

            return BuildResponse(statusCode, message, code);
        }

        private static ObjectResult BuildResponse(int statusCode, string message, string code) =>
            new(ApiResponse<object?>.Failure(message, code))
            {
                StatusCode = statusCode,
            };
    }
}
