using backend.main.features.auth.history;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.History;

public class SecurityHistoryClientsTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36", SecurityHistoryClient.Chrome)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/130.0 Safari/537.36 Edg/130.0", SecurityHistoryClient.Edge)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0", SecurityHistoryClient.Firefox)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15", SecurityHistoryClient.Safari)]
    [InlineData("Mozilla/5.0 Chrome/130.0 Safari/537.36 OPR/114.0", SecurityHistoryClient.Opera)]
    [InlineData("curl/8.9.1", SecurityHistoryClient.ApiClient)]
    [InlineData("PostmanRuntime/7.42.0", SecurityHistoryClient.ApiClient)]
    [InlineData("", SecurityHistoryClient.Unknown)]
    [InlineData(null, SecurityHistoryClient.Unknown)]
    public void FromUserAgent_ShouldRecogniseKnownFamilies(string? userAgent, SecurityHistoryClient expected)
    {
        SecurityHistoryClients.FromUserAgent(userAgent).Should().Be(expected);
    }

    [Fact]
    public void FromUserAgent_ShouldNeverKeepAnUnrecognisedProductToken()
    {
        // The classifier would echo "<script>alert(1)</script>"; the history keeps a closed value.
        SecurityHistoryClients.FromUserAgent("<script>alert(1)</script>/1.0").Should().Be(SecurityHistoryClient.Other);
    }
}
