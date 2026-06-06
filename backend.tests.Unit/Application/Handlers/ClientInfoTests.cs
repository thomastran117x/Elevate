using System.Net;

using backend.main.application.handlers;
using backend.main.shared.requests;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Application.Handlers;

public class ClientInfoTests
{
    [Fact]
    public void AddClientRequestInspection_ShouldRegisterScopedClientRequestInfo()
    {
        var services = new ServiceCollection();

        services.AddClientRequestInspection();

        var descriptor = services.Should()
            .ContainSingle(service => service.ServiceType == typeof(ClientRequestInfo))
            .Subject;

        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task Middleware_ShouldPopulateUnknownDefaults_WhenUserAgentIsMissing()
    {
        ClientRequestInfo? captured = null;
        var middleware = new ClientRequestInspectionMiddleware(_ =>
        {
            captured = _.RequestServices.GetRequiredService<ClientRequestInfo>();
            return Task.CompletedTask;
        });

        var services = new ServiceCollection()
            .AddScoped<ClientRequestInfo>()
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };

        await middleware.InvokeAsync(context, scope.ServiceProvider.GetRequiredService<ClientRequestInfo>());

        captured.Should().NotBeNull();
        captured!.IpAddress.Should().Be("Unknown");
        captured.ClientName.Should().Be("Unknown");
        captured.DeviceType.Should().Be("Unknown");
        captured.IsBrowserClient.Should().BeTrue();
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36", "Chrome", "Desktop", true)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 16_0 like Mac OS X) AppleWebKit/605.1.15 Version/16.0 Safari/604.1", "Safari", "Tablet", true)]
    [InlineData("PostmanRuntime/7.43.0", "Postman", "API Client", false)]
    [InlineData("axios/1.7.2", "Axios", "API Client", false)]
    [InlineData("curl/8.7.1", "cURL", "API Client", false)]
    [InlineData("CustomClient/1.0", "CustomClient", "Unknown", true)]
    public async Task Middleware_ShouldResolveClientMetadata_FromUserAgent(
        string userAgent,
        string expectedClient,
        string expectedDevice,
        bool expectedBrowser)
    {
        var requestInfo = new ClientRequestInfo();
        var middleware = new ClientRequestInspectionMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers.UserAgent = userAgent;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

        await middleware.InvokeAsync(context, requestInfo);

        requestInfo.IpAddress.Should().Be("203.0.113.9");
        requestInfo.ClientName.Should().Be(expectedClient);
        requestInfo.DeviceType.Should().Be(expectedDevice);
        requestInfo.IsBrowserClient.Should().Be(expectedBrowser);
    }

    [Fact]
    public void UseClientRequestInspection_ShouldReturnApplicationBuilder()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var result = app.UseClientRequestInspection();

        result.Should().BeSameAs(app);
    }
}
