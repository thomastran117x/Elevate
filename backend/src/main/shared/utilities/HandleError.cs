using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

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
                "INTERNAL_SERVER_ERROR",
                IsTestingEnvironment() ? ex.ToString() : null
            );

            return new ObjectResult(response)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        private static bool IsTestingEnvironment() =>
            string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Testing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Testing", StringComparison.OrdinalIgnoreCase);

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

