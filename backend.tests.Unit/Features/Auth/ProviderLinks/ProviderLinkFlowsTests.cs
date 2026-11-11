using backend.main.application.environment;
using backend.main.features.auth.oauth;
using backend.main.features.auth.oauth.links;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.AspNetCore.WebUtilities;

using Moq;

namespace backend.tests.Unit.Features.Auth.ProviderLinks;

public class ProviderLinkFlowsTests
{
    private readonly Mock<IOAuthService> _oauth = new();

    [Fact]
    public void Google_ShouldSendTheServersStateNonceAndChallenge_ToItsOwnCallback()
    {
        var flow = new GoogleProviderLinkFlow(_oauth.Object);

        var query = Query(flow.BuildAuthorizationUrl("the-state", "the-nonce", "the-challenge"));

        query["state"].Should().Be("the-state");
        query["nonce"].Should().Be("the-nonce");
        query["code_challenge"].Should().Be("the-challenge");
        query["code_challenge_method"].Should().Be("S256");
        query["response_type"].Should().Be("code");
        query["redirect_uri"].Should().Be(flow.RedirectUri);
        flow.RedirectUri.Should().Be($"{EnvironmentSetting.FrontendBaseUrl.TrimEnd('/')}/auth/google");
        flow.BrowserRedeemsCode.Should().BeFalse();
    }

    [Fact]
    public void Microsoft_ShouldAskForAQueryResponse_ToItsOwnCallback()
    {
        var flow = new MicrosoftProviderLinkFlow(_oauth.Object);

        var query = Query(flow.BuildAuthorizationUrl("the-state", "the-nonce", "the-challenge"));

        query["response_mode"].Should().Be("query");
        query["redirect_uri"].Should().EndWith("/auth/microsoft");
        query["nonce"].Should().Be("the-nonce");
        flow.BrowserRedeemsCode.Should().BeTrue();
    }

    [Fact]
    public void Flows_ShouldReportAvailability_FromTheOAuthConfiguration()
    {
        _oauth.Setup(o => o.IsProviderConfigured("google")).Returns(true);
        _oauth.Setup(o => o.IsProviderConfigured("microsoft")).Returns(false);

        new GoogleProviderLinkFlow(_oauth.Object).IsAvailable.Should().BeTrue();
        new MicrosoftProviderLinkFlow(_oauth.Object).IsAvailable.Should().BeFalse();
    }

    /// <summary>The verifier and redirect come from the stored state, never from the request.</summary>
    [Fact]
    public async Task Google_ShouldRedeemTheCodeWithTheStoredVerifier_AndCheckTheStoredNonce()
    {
        var expected = new OAuthUser("google-1", "a@example.com", "A", "google", true);
        _oauth.Setup(o => o.ExchangeGoogleCodeAsync("code", "stored-verifier", "https://app.example/auth/google"))
            .ReturnsAsync("id-token");
        _oauth.Setup(o => o.VerifyGoogleTokenAsync("id-token", "stored-nonce")).ReturnsAsync(expected);

        var identity = await new GoogleProviderLinkFlow(_oauth.Object).ResolveIdentityAsync(State(), "code", "ignored-token");

        identity.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Microsoft_ShouldVerifyThePostedToken_AgainstTheStoredNonce()
    {
        var expected = new OAuthUser("ms-1", "a@example.com", "A", "microsoft", true);
        _oauth.Setup(o => o.VerifyMicrosoftTokenAsync("id-token", "stored-nonce")).ReturnsAsync(expected);

        var identity = await new MicrosoftProviderLinkFlow(_oauth.Object).ResolveIdentityAsync(State(), null, "id-token");

        identity.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Flows_ShouldRefuse_ACallbackMissingWhatTheyNeed()
    {
        await new GoogleProviderLinkFlow(_oauth.Object)
            .Invoking(f => f.ResolveIdentityAsync(State(), null, "id-token"))
            .Should().ThrowAsync<BadRequestException>();
        await new MicrosoftProviderLinkFlow(_oauth.Object)
            .Invoking(f => f.ResolveIdentityAsync(State(), "code", null))
            .Should().ThrowAsync<BadRequestException>();
    }

    [Theory]
    [InlineData("google", "google")]
    [InlineData(" Microsoft ", "microsoft")]
    public void TryParseLinkable_ShouldNormalizeKnownProviders(string value, string expected)
    {
        IdentityProviders.TryParseLinkable(value, out var provider).Should().BeTrue();
        provider.Should().Be(expected);
    }

    [Theory]
    [InlineData("apple")]
    [InlineData("facebook")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseLinkable_ShouldRefuseAnythingElse(string? value)
    {
        IdentityProviders.TryParseLinkable(value, out _).Should().BeFalse();
    }

    private static Dictionary<string, string> Query(string url) =>
        QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());

    private static ProviderLinkState State() => new()
    {
        Provider = IdentityProviders.Google,
        UserId = 1,
        SessionId = "session",
        AuthVersion = 1,
        Nonce = "stored-nonce",
        CodeVerifier = "stored-verifier",
        RedirectUri = "https://app.example/auth/google",
        CreatedAtUtc = DateTime.UtcNow,
    };
}
