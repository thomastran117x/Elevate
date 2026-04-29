using backend.main.configurations.application;
using backend.main.utilities.implementation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace backend.test;

public class HttpUtilityTests
{
    [Fact]
    public void SetBrowserRefreshSession_UsesTheApiAuthPathForBothCookies()
    {
        var httpContext = new DefaultHttpContext();

        HttpUtility.SetBrowserRefreshSession(
            httpContext.Response,
            "refresh-token-value",
            "binding-token-value",
            TimeSpan.FromMinutes(30)
        );

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();

        setCookieHeader.Should().Contain($"{HttpUtility.RefreshCookieName}=refresh-token-value");
        setCookieHeader.Should().Contain($"{HttpUtility.RefreshBindingCookieName}=binding-token-value");
        setCookieHeader.Should().Contain($"path={RoutePaths.ApiAuthPath}");
    }

    [Fact]
    public void ClearBrowserRefreshSession_UsesTheApiAuthPathForBothCookies()
    {
        var httpContext = new DefaultHttpContext();

        HttpUtility.ClearBrowserRefreshSession(httpContext.Response);

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();

        setCookieHeader.Should().Contain($"{HttpUtility.RefreshCookieName}=;");
        setCookieHeader.Should().Contain($"{HttpUtility.RefreshBindingCookieName}=;");
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

    [Fact]
    public void ResolveApiSessionBindingToken_PrefersHeaderValue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HttpUtility.SessionBindingHeaderName] = "header-binding-token";

        var resolved = HttpUtility.ResolveApiSessionBindingToken(
            httpContext.Request,
            "body-binding-token"
        );

        resolved.Should().Be("header-binding-token");
    }

    [Fact]
    public void ResolveBrowserRefreshToken_ReadsCookieOnly()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{HttpUtility.RefreshCookieName}=refresh-cookie-token; {HttpUtility.RefreshBindingCookieName}=binding-token";

        var resolved = HttpUtility.ResolveBrowserRefreshToken(httpContext.Request);

        resolved.Should().Be("refresh-cookie-token");
    }
}
