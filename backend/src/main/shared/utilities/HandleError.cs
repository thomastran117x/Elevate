using backend.main.dtos.responses.general;
using backend.main.shared.exceptions.http;

using Microsoft.AspNetCore.Mvc;

namespace backend.main.utilities
{
    public static class HandleError
    {
        public static IActionResult Resolve(Exception ex)
        {
            if (ex is AppException appEx)
                return HandleAppException(appEx);

            var response = ApiResponse<object?>.Failure(
                "An unexpected error occurred.",
                "INTERNAL_SERVER_ERROR"
            );

            return new ObjectResult(response)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        private static IActionResult HandleAppException(AppException ex)
        {
            var response = ApiResponse<object?>.Failure(ex.Message, ex.ErrorCode, ex.Details);

            return new ObjectResult(response)
            {
                StatusCode = ex.StatusCode
            };
        }
    }
}
