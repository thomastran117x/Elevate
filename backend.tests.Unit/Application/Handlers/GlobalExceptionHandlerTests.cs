using System.Text.Json;

using backend.main.application.handlers;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.AspNetCore.Antiforgery;

namespace backend.tests.Unit.Application.Handlers;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task InvokeAsync_ShouldReturnBadRequestPayload_ForAntiforgeryFailures()
    {
        var context = CreateContext();
        var handler = new GlobalExceptionHandler(_ => throw new AntiforgeryValidationException("token missing"));

        await handler.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        using var json = await ReadJsonAsync(context);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("CSRF_VALIDATION_FAILED");
        json.RootElement.GetProperty("error").GetProperty("details").GetProperty("reason").GetString()
            .Should().Be("token missing");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnResolvedAppExceptionPayload()
    {
        var context = CreateContext();
        var handler = new GlobalExceptionHandler(_ => throw new BadRequestException("Bad input", "field"));

        await handler.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        using var json = await ReadJsonAsync(context);
        json.RootElement.GetProperty("message").GetString().Should().Be("Bad input");
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("BAD_REQUEST");
        json.RootElement.GetProperty("error").GetProperty("details").GetString().Should().Be("field");
    }

    [Fact]
    public async Task InvokeAsync_ShouldReturnGenericInternalServerErrorPayload_ForUnhandledExceptions()
    {
        var context = CreateContext();
        var handler = new GlobalExceptionHandler(_ => throw new InvalidOperationException("boom"));

        await handler.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        using var json = await ReadJsonAsync(context);
        json.RootElement.GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("INTERNAL_SERVER_ERROR");
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadJsonAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
