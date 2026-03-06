using backend.main.dtos.responses.general;
using backend.main.exceptions.http;

using Microsoft.AspNetCore.Mvc;

namespace backend.main.utilities.implementation
{
    public static class HandleError
    {
        public static IActionResult Resolve(Exception ex)
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
