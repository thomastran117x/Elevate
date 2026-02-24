using backend.main.DTOs;
using backend.main.Exceptions;

using Microsoft.AspNetCore.Mvc;

namespace backend.main.Utilities
{
    public static class ErrorUtility
    {
        public static IActionResult HandleError(Exception ex)
        {
            if (ex is AppException appEx)
                return HandleAppException(appEx);

            MessageResponse response = new MessageResponse(
                "An unexpected error occurred."
            );

            return new ObjectResult(response)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        private static IActionResult HandleAppException(AppException ex)
        {
            var response = new MessageResponse(ex.Message, ex.Details);

            return new ObjectResult(response)
            {
                StatusCode = ex.StatusCode
            };
        }
    }
}
