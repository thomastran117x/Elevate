using backend.main.shared.requests;
using backend.main.utilities;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Utilities;

public class HttpUtilityTests
{
    [Fact]
    public void ResolveBrowserAndApiRefreshTokens_ShouldPreferCookiesAndHeaders()
    {
        var browserContext = new DefaultHttpContext();
        browserContext.Request.Headers.Cookie = $"{HttpUtility.RefreshCookieName}=cookie-refresh";

        var apiContext = new DefaultHttpContext();
        apiContext.Request.Headers[HttpUtility.RefreshTokenHeaderName] = "header-refresh";

        HttpUtility.ResolveBrowserRefreshToken(browserContext.Request).Should().Be("cookie-refresh");
        HttpUtility.ResolveApiRefreshToken(apiContext.Request, "body-refresh").Should().Be("header-refresh");
        HttpUtility.ResolveApiRefreshToken(new DefaultHttpContext().Request, "body-refresh").Should().Be("body-refresh");
        HttpUtility.ResolveApiRefreshToken(new DefaultHttpContext().Request).Should().BeNull();
    }

    [Fact]
    public void ResolveSessionBindingAndTrustedDeviceTokens_ShouldUseAvailableSources()
    {
        var cookieContext = new DefaultHttpContext();
        cookieContext.Request.Headers.Cookie =
            $"{HttpUtility.RefreshBindingCookieName}=cookie-binding; {HttpUtility.TrustedDeviceCookieName}=cookie-device";

        var headerContext = new DefaultHttpContext();
        headerContext.Request.Headers[HttpUtility.SessionBindingHeaderName] = "header-binding";
        headerContext.Request.Headers[HttpUtility.TrustedDeviceHeaderName] = "header-device";

        HttpUtility.ResolveBrowserSessionBindingToken(cookieContext.Request).Should().Be("cookie-binding");
        HttpUtility.ResolveApiSessionBindingToken(headerContext.Request, "body-binding").Should().Be("header-binding");
        HttpUtility.ResolveApiSessionBindingToken(new DefaultHttpContext().Request, "body-binding").Should().Be("body-binding");
        HttpUtility.ResolveTrustedDeviceToken(cookieContext.Request, "body-device").Should().Be("cookie-device");
        HttpUtility.ResolveTrustedDeviceToken(headerContext.Request, "body-device").Should().Be("header-device");
        HttpUtility.ResolveTrustedDeviceToken(new DefaultHttpContext().Request, "body-device").Should().Be("body-device");
    }

    [Fact]
    public void SetAndClearBrowserRefreshSession_ShouldManageCookies()
    {
        var context = new DefaultHttpContext();

        HttpUtility.SetBrowserRefreshSession(
            context.Response,
            "refresh-token",
            "binding-token",
            TimeSpan.FromHours(2));
        var setCookies = context.Response.Headers.SetCookie.ToArray();

        setCookies.Should().HaveCount(2);
        setCookies.Should().Contain(cookie => cookie.Contains($"{HttpUtility.RefreshCookieName}=refresh-token"));
        setCookies.Should().Contain(cookie => cookie.Contains($"{HttpUtility.RefreshBindingCookieName}=binding-token"));
        setCookies.Should().OnlyContain(cookie =>
            cookie.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));

        context.Response.Headers.Remove("Set-Cookie");

        HttpUtility.ClearBrowserRefreshSession(context.Response);
        var clearedCookies = context.Response.Headers.SetCookie.ToArray();

        clearedCookies.Should().HaveCount(2);
        clearedCookies.Should().OnlyContain(cookie =>
            cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase)
            || cookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)
            || cookie.Contains("max-age=-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetAndClearTrustedDeviceToken_ShouldRespectBrowserClientFlag()
    {
        var browserContext = new DefaultHttpContext();
        var browserRequest = new ClientRequestInfo { IsBrowserClient = true };

        HttpUtility.SetTrustedDeviceToken(browserContext.Response, browserRequest, "device-token", TimeSpan.FromDays(1));

        browserContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName].ToString().Should().Be("device-token");
        browserContext.Response.Headers.SetCookie.Should().Contain(cookie =>
            cookie.Contains($"{HttpUtility.TrustedDeviceCookieName}=device-token")
            && cookie.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase));

        browserContext.Response.Headers.Remove("Set-Cookie");
        HttpUtility.ClearTrustedDeviceToken(browserContext.Response, browserRequest);
        browserContext.Response.Headers.ContainsKey(HttpUtility.TrustedDeviceHeaderName).Should().BeFalse();
        browserContext.Response.Headers.SetCookie.Should().Contain(cookie =>
            cookie.Contains($"{HttpUtility.TrustedDeviceCookieName}=", StringComparison.OrdinalIgnoreCase));

        var apiContext = new DefaultHttpContext();
        var apiRequest = new ClientRequestInfo { IsBrowserClient = false };

        HttpUtility.SetTrustedDeviceToken(apiContext.Response, apiRequest, "api-device");
        apiContext.Response.Headers[HttpUtility.TrustedDeviceHeaderName].ToString().Should().Be("api-device");
        apiContext.Response.Headers.SetCookie.Should().BeEmpty();

        HttpUtility.ClearTrustedDeviceToken(apiContext.Response, apiRequest);
        apiContext.Response.Headers.ContainsKey(HttpUtility.TrustedDeviceHeaderName).Should().BeFalse();
        apiContext.Response.Headers.SetCookie.Should().BeEmpty();
    }
}
