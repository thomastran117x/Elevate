using System.Security.Claims;

using backend.main.application.security;
using backend.main.features.auth;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.token;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

using Moq;

namespace backend.tests.Unit.Application.Security;

public class RequireMfaFilterTests
{
    private const string SeedEmail = "organizer@seed.eventxperience.test";

    [Fact]
    public async Task OnAuthorization_ShouldAllow_WhenSessionAlreadyVerified()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.IsSessionVerifiedAsync("session-1")).ReturnsAsync(true);

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: "session-1");

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbidWithMfaRequiredCode_WhenSessionNotVerified()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.IsSessionVerifiedAsync(It.IsAny<string?>())).ReturnsAsync(false);

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: "session-1");

        await filter.OnAuthorizationAsync(context);

        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = result.Value.Should().BeOfType<ApiResponse<object?>>().Subject;
        body.Success.Should().BeFalse();
        body.Error!.Code.Should().Be("MFA_REQUIRED");
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbid_WhenAccessTokenHasNoSessionId()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.IsSessionVerifiedAsync(null)).ReturnsAsync(false);

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: null);

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task OnAuthorization_ShouldAllow_ForSeedBypassAccount_WithoutCallingVerification()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: true);
        var context = CreateContext(email: SeedEmail, sessionId: null);

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        sessionMfa.Verify(s => s.IsSessionVerifiedAsync(It.IsAny<string?>()), Times.Never);
    }

    private static RequireMfaAttribute.RequireMfaFilter CreateFilter(
        ISessionMfaVerificationService sessionMfa,
        bool bypassEnabled)
    {
        var values = new Dictionary<string, string?>
        {
            ["ENVIRONMENT"] = "development",
        };
        if (bypassEnabled)
        {
            values["AUTH_SEED_ACCOUNT_BYPASS"] = "true";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new RequireMfaAttribute.RequireMfaFilter(sessionMfa, new SeedAccountBypassPolicy(configuration));
    }

    private static AuthorizationFilterContext CreateContext(string email, string? sessionId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Role, "Participant"),
        };
        if (sessionId != null)
        {
            claims.Add(new Claim(TokenService.SessionIdClaimType, sessionId));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }
}
