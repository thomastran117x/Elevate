using System.Net;
using System.Text;

using backend.main.features.auth.captcha;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class CloudflareTurnstileCaptchaServiceTests
{
    [Fact]
    public async Task VerifyCaptchaAsync_ShouldAllowBypass_InNonProduction()
    {
        var service = CreateService(
            configValues: new Dictionary<string, string?>
            {
                ["Captcha:AllowBypass"] = "true",
                ["ENVIRONMENT"] = "test"
            });

        var result = await service.VerifyCaptchaAsync("ignored");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldRejectMissingToken()
    {
        var service = CreateService();

        var result = await service.VerifyCaptchaAsync(" ");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldRejectWhenSecretIsMissing()
    {
        var service = CreateService(
            configValues: new Dictionary<string, string?>
            {
                ["ENVIRONMENT"] = "production",
                ["Turnstile:Secret"] = ""
            });

        var result = await service.VerifyCaptchaAsync("token");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldRejectWhenResponseStatusIsNotSuccessful()
    {
        var service = CreateService(
            responseFactory: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request", Encoding.UTF8, "text/plain")
            });

        var result = await service.VerifyCaptchaAsync("token");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldRejectWhenPayloadIsMalformed()
    {
        var service = CreateService(
            responseFactory: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            });

        var result = await service.VerifyCaptchaAsync("token");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldRejectWhenPayloadSuccessIsFalse()
    {
        var service = CreateService(
            responseFactory: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"success":false,"error-codes":["timeout-or-duplicate"]}""",
                    Encoding.UTF8,
                    "application/json")
            });

        var result = await service.VerifyCaptchaAsync("token");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldReturnTrue_AndIncludeRemoteIpInRequest()
    {
        string? capturedBody = null;
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        accessor.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        var service = CreateService(
            responseFactory: request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"success":true}""", Encoding.UTF8, "application/json")
                };
            },
            httpContextAccessor: accessor);

        var result = await service.VerifyCaptchaAsync("token");

        result.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("secret=test-secret");
        capturedBody.Should().Contain("response=token");
        capturedBody.Should().Contain("remoteip=203.0.113.10");
    }

    [Fact]
    public async Task VerifyCaptchaAsync_ShouldRejectWhenRequestThrows()
    {
        var service = CreateService(
            handlerException: new HttpRequestException("network down"));

        var result = await service.VerifyCaptchaAsync("token");

        result.Should().BeFalse();
    }

    private static CloudflareTurnstileCaptchaService CreateService(
        Dictionary<string, string?>? configValues = null,
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null,
        Exception? handlerException = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Turnstile:Secret"] = "test-secret",
            ["ENVIRONMENT"] = "production",
            ["Turnstile:TimeoutSeconds"] = "7"
        };

        if (configValues != null)
        {
            foreach (var pair in configValues)
            {
                values[pair.Key] = pair.Value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var handler = new StubHttpMessageHandler(responseFactory, handlerException);
        var httpClient = new HttpClient(handler);

        return new CloudflareTurnstileCaptchaService(
            httpClient,
            Mock.Of<ILogger<CloudflareTurnstileCaptchaService>>(),
            configuration,
            httpContextAccessor);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;
        private readonly Exception? _handlerException;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage>? responseFactory,
            Exception? handlerException)
        {
            _responseFactory = responseFactory ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"success":true}""", Encoding.UTF8, "application/json")
            });
            _handlerException = handlerException;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_handlerException != null)
                throw _handlerException;

            return Task.FromResult(_responseFactory(request));
        }
    }
}
