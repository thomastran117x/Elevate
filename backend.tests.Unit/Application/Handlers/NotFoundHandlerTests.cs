using System.Text.Json;

using backend.main.application.handlers;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Application.Handlers;

public class NotFoundHandlerTests
{
    [Fact]
    public async Task UseJsonNotFound_ShouldReturnStructured404Payload()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseJsonNotFound();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        var pipeline = app.Build();
        var context = new DefaultHttpContext();
        context.Request.Path = "/missing/resource";
        context.Response.Body = new MemoryStream();

        await pipeline(context);

        context.Response.ContentType.Should().StartWith("application/json");
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("message").GetString().Should().Be("Resource not found.");
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("RESOURCE_NOT_FOUND");
        json.RootElement.GetProperty("error").GetProperty("details").GetProperty("path").GetString()
            .Should().Be("/missing/resource");
    }
}
