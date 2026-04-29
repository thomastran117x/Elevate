using System.Net;
using System.Text;
using backend.main.configurations.application;
using backend.main.services.implementations;
using backend.test.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace backend.test;

public class CaptchaServiceTests
{
    [Fact]
    public async Task GoogleCaptcha_ReturnsFalse_ForExplicitInvalidToken()
    {
        var service = CreateGoogleService(
            configValues: new Dictionary<string, string?>
            {
                ["GoogleCaptcha:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """{"success":false,"error-codes":["invalid-input-response"]}"""
            ))
        );

        var isValid = await service.VerifyCaptchaAsync("bad-token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleCaptcha_ReturnsFalse_WhenProviderReturnsHttpFailure()
    {
        var service = CreateGoogleService(
            configValues: new Dictionary<string, string?>
            {
                ["GoogleCaptcha:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.BadGateway,
                """{"success":false}"""
            ))
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleCaptcha_ReturnsFalse_WhenResponseIsMalformed()
    {
        var service = CreateGoogleService(
            configValues: new Dictionary<string, string?>
            {
                ["GoogleCaptcha:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
            })
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleCaptcha_ReturnsFalse_WhenSecretIsMissingInProduction()
    {
        var service = CreateGoogleService(
            configValues: new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not call provider"))
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GoogleCaptcha_ReturnsTrue_WhenProviderApprovesToken()
    {
        var service = CreateGoogleService(
            configValues: new Dictionary<string, string?>
            {
                ["GoogleCaptcha:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse(
                HttpStatusCode.OK,
                """{"success":true}"""
            ))
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task GoogleCaptcha_AllowsExplicitBypass_OnlyInNonProduction()
    {
        var service = CreateGoogleService(
            configValues: new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "development",
                ["Captcha:AllowBypass"] = "true"
            },
            handler: new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not call provider"))
        );

        var isValid = await service.VerifyCaptchaAsync("any-token");

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task TurnstileCaptcha_ReturnsFalse_WhenProviderThrows()
    {
        var service = CreateTurnstileService(
            configValues: new Dictionary<string, string?>
            {
                ["Turnstile:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => throw new HttpRequestException("network failure"))
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task TurnstileCaptcha_ReturnsFalse_WhenResponseIsMalformed()
    {
        var service = CreateTurnstileService(
            configValues: new Dictionary<string, string?>
            {
                ["Turnstile:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{oops", Encoding.UTF8, "application/json")
            })
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task TurnstileCaptcha_ReturnsFalse_WhenSecretIsMissingInProduction()
    {
        var service = CreateTurnstileService(
            configValues: new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            },
            handler: new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not call provider"))
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task TurnstileCaptcha_ReturnsTrue_AndSendsRemoteIp_WhenProviderApprovesToken()
    {
        string? sentBody = null;
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.42");

        var service = new CloudflareTurnstileCaptchaService(
            new HttpClient(new StubHttpMessageHandler(request =>
            {
                sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return StubHttpMessageHandler.JsonResponse(
                    HttpStatusCode.OK,
                    """{"success":true}"""
                );
            })),
            NullLogger<CloudflareTurnstileCaptchaService>.Instance,
            BuildConfig(new Dictionary<string, string?>
            {
                ["Turnstile:Secret"] = "secret",
                ["ASPNETCORE_ENVIRONMENT"] = "production"
            }),
            new Microsoft.AspNetCore.Http.HttpContextAccessor
            {
                HttpContext = httpContext
            }
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeTrue();
        sentBody.Should().Contain("remoteip=203.0.113.42");
    }

    [Fact]
    public async Task TurnstileCaptcha_AllowsExplicitBypass_OnlyInNonProduction()
    {
        var service = CreateTurnstileService(
            configValues: new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "development",
                ["Captcha:AllowBypass"] = "true"
            },
            handler: new StubHttpMessageHandler(_ => throw new InvalidOperationException("should not call provider"))
        );

        var isValid = await service.VerifyCaptchaAsync("token");

        isValid.Should().BeTrue();
    }

    [Fact]
    public void DependencyInjection_ConfiguresGoogleCaptchaBaseAddress()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddApplicationServices(config);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var googleService = scope.ServiceProvider.GetRequiredService<GoogleCaptchaService>();

        var httpClient = (HttpClient)typeof(GoogleCaptchaService)
            .GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(googleService)!;

        httpClient.BaseAddress.Should().Be(new Uri("https://www.google.com/"));
    }

    private static GoogleCaptchaService CreateGoogleService(
        Dictionary<string, string?> configValues,
        HttpMessageHandler handler
    )
    {
        return new GoogleCaptchaService(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://www.google.com/")
            },
            NullLogger<GoogleCaptchaService>.Instance,
            BuildConfig(configValues)
        );
    }

    private static CloudflareTurnstileCaptchaService CreateTurnstileService(
        Dictionary<string, string?> configValues,
        HttpMessageHandler handler
    )
    {
        return new CloudflareTurnstileCaptchaService(
            new HttpClient(handler),
            NullLogger<CloudflareTurnstileCaptchaService>.Instance,
            BuildConfig(configValues)
        );
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

}
