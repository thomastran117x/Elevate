using backend.main.application.handlers;
using backend.main.shared.requests;

using FluentAssertions;

namespace backend.tests.Unit.Application.Handlers;

public class ClientRequestInspectionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldClassifyDesktopBrowserClients()
    {
        var requestInfo = new ClientRequestInfo();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        context.Request.Headers.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36";

        var middleware = new ClientRequestInspectionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, requestInfo);

        requestInfo.IpAddress.Should().Be("203.0.113.10");
        requestInfo.ClientName.Should().Be("Chrome");
        requestInfo.DeviceType.Should().Be("Desktop");
        requestInfo.IsBrowserClient.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldClassifyApiClients()
    {
        var requestInfo = new ClientRequestInfo();
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = "PostmanRuntime/7.43.0";

        var middleware = new ClientRequestInspectionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, requestInfo);

        requestInfo.ClientName.Should().Be("Postman");
        requestInfo.DeviceType.Should().Be("API Client");
        requestInfo.IsBrowserClient.Should().BeFalse();
    }
}
