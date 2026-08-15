using backend.main.application.handlers;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace backend.tests.Unit.Application.Handlers;

public class ApiClientErrorFactoryTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "The request was invalid.", "BAD_REQUEST")]
    [InlineData(StatusCodes.Status401Unauthorized, "Authentication is required.", "UNAUTHORIZED")]
    [InlineData(StatusCodes.Status403Forbidden, "You do not have permission to perform this action.", "FORBIDDEN")]
    [InlineData(StatusCodes.Status404NotFound, "The requested resource was not found.", "NOT_FOUND")]
    [InlineData(StatusCodes.Status405MethodNotAllowed, "This action is not allowed.", "METHOD_NOT_ALLOWED")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, "The request format is not supported.", "UNSUPPORTED_MEDIA_TYPE")]
    [InlineData(StatusCodes.Status418ImATeapot, "The request could not be completed.", "REQUEST_ERROR")]
    public void GetClientError_ShouldMapStatusToApiResponse(
        int statusCode,
        string expectedMessage,
        string expectedCode
    )
    {
        var result = new ApiClientErrorFactory().GetClientError(
            new ActionContext(),
            new StatusCodeResult(statusCode)
        );

        AssertResponse(result, statusCode, expectedMessage, expectedCode);
    }

    [Fact]
    public void GetClientError_ShouldDefaultMissingStatusToInternalServerError()
    {
        var result = new ApiClientErrorFactory().GetClientError(
            new ActionContext(),
            new ClientErrorResult(null)
        );

        AssertResponse(
            result,
            StatusCodes.Status500InternalServerError,
            "The request could not be completed.",
            "REQUEST_ERROR"
        );
    }

    [Fact]
    public void GetClientError_ShouldReturnSpecificCsrfFailure()
    {
        var result = new ApiClientErrorFactory().GetClientError(
            new ActionContext(),
            new AntiforgeryValidationFailedResult()
        );

        AssertResponse(
            result,
            StatusCodes.Status400BadRequest,
            "CSRF validation failed. Refresh the page and try again.",
            "CSRF_VALIDATION_FAILED"
        );
    }

    private static void AssertResponse(
        IActionResult? result,
        int expectedStatusCode,
        string expectedMessage,
        string expectedCode
    )
    {
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);

        var response = objectResult.Value.Should().BeOfType<ApiResponse<object?>>().Subject;
        response.Success.Should().BeFalse();
        response.Message.Should().Be(expectedMessage);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(expectedCode);
    }

    private class ClientErrorResult(int? statusCode) : IActionResult, IClientErrorActionResult
    {
        public int? StatusCode { get; } = statusCode;

        public Task ExecuteResultAsync(ActionContext context) => Task.CompletedTask;
    }

    private sealed class AntiforgeryValidationFailedResult()
        : ClientErrorResult(StatusCodes.Status403Forbidden);
}
