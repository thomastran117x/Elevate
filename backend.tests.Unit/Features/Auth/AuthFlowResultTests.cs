using backend.main.features.auth;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth;

public class AuthFlowResultTests
{
    [Fact]
    public void LoginAuthenticationResult_RequiresStepUp_ShouldSetTypeAndStepUp()
    {
        var stepUp = new LoginStepUpChallengeResponse
        {
            Challenge = "ch",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            AvailableMethods = ["email"],
            MaskedEmail = "u***@example.com"
        };

        var result = LoginAuthenticationResult.RequiresStepUp(stepUp);

        result.Type.Should().Be("requires_step_up");
        result.StepUp.Should().BeSameAs(stepUp);
        result.Session.Should().BeNull();
    }

    [Fact]
    public void OAuthAuthenticationResult_RequiresStepUp_ShouldSetTypeAndStepUp()
    {
        var stepUp = new LoginStepUpChallengeResponse
        {
            Challenge = "ch",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            AvailableMethods = ["email"],
            MaskedEmail = "u***@example.com"
        };

        var result = OAuthAuthenticationResult.RequiresStepUp(stepUp);

        result.Type.Should().Be("requires_step_up");
        result.StepUp.Should().BeSameAs(stepUp);
        result.Session.Should().BeNull();
        result.UserToken.Should().BeNull();
    }

    [Fact]
    public void SessionTransportResolver_ShouldResolveBrowserValue()
    {
        var result = SessionTransportResolver.ResolveOrDefault(SessionTransportResolver.BrowserValue);

        result.Should().Be(SessionTransport.BrowserCookie);
        result.UsesBrowserCookies().Should().BeTrue();
    }

    [Fact]
    public void SessionTransportResolver_ShouldThrow_WhenTransportIsUnknown()
    {
        var act = () => SessionTransportResolver.ResolveOrDefault("fax");

        act.Should().Throw<BadRequestException>();
    }
}
