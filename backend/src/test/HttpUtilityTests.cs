using backend.main.configurations.application;
using backend.main.utilities.implementation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace backend.test;

public class HttpUtilityTests
{
    [Fact]
    public void SetRefreshTokenCookie_UsesTheApiAuthPath()
    {
        var httpContext = new DefaultHttpContext();

        HttpUtility.SetRefreshTokenCookie(
            httpContext.Response,
            "refresh-token-value",
            TimeSpan.FromMinutes(30)
        );

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();

        setCookieHeader.Should().Contain($"{HttpUtility.RefreshCookieName}=refresh-token-value");
        setCookieHeader.Should().Contain($"path={RoutePaths.ApiAuthPath}");
    }

    [Fact]
    public void ClearRefreshTokenCookie_UsesTheApiAuthPath()
    {
        var httpContext = new DefaultHttpContext();

        HttpUtility.ClearRefreshTokenCookie(httpContext.Response);

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();

        setCookieHeader.Should().Contain($"{HttpUtility.RefreshCookieName}=;");
        setCookieHeader.Should().Contain($"path={RoutePaths.ApiAuthPath}");
    }

    [Fact]
    public void SetTrustedDeviceToken_UsesTheApiAuthPathAndHeader()
    {
        var httpContext = new DefaultHttpContext();

        HttpUtility.SetTrustedDeviceToken(
            httpContext.Response,
            new backend.main.dtos.general.ClientRequestInfo { IsBrowserClient = true },
            "trusted-device-token",
            TimeSpan.FromDays(30)
        );

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();

        httpContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName]
            .ToString().Should().Be("trusted-device-token");
        setCookieHeader.Should().Contain($"{HttpUtility.TrustedDeviceCookieName}=trusted-device-token");
        setCookieHeader.Should().Contain($"path={RoutePaths.ApiAuthPath}");
    }
}
