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
    public async Task OnAuthorization_ShouldAllow_WhenSessionHasFreshProof()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.HasFreshProofAsync(42, "session-1", 5)).ReturnsAsync(true);

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: "session-1");

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbidWithMfaRequiredCode_WhenNoFreshProof()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.HasFreshProofAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(false);

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: "session-1");

        await filter.OnAuthorizationAsync(context);

        AssertMfaRequired(context);
    }

    [Fact]
    public async Task OnAuthorization_ShouldPassTokenBindingClaimsToProofCheck()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: "session-9", authVersion: "11");

        await filter.OnAuthorizationAsync(context);

        sessionMfa.Verify(s => s.HasFreshProofAsync(42, "session-9", 11), Times.Once);
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbid_WhenAccessTokenHasNoSessionId()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();
        sessionMfa.Setup(s => s.HasFreshProofAsync(42, null, 5)).ReturnsAsync(false);

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: null);

        await filter.OnAuthorizationAsync(context);

        AssertMfaRequired(context);
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbid_WithoutCheckingProof_WhenAuthVersionClaimMissing()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: false);
        var context = CreateContext(email: "member@example.com", sessionId: "session-1", authVersion: null);

        await filter.OnAuthorizationAsync(context);

        AssertMfaRequired(context);
        sessionMfa.Verify(
            s => s.HasFreshProofAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorization_ShouldAllow_ForSeedBypassAccount_WithoutCallingVerification()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: true);
        var context = CreateContext(email: SeedEmail, sessionId: null);

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        sessionMfa.Verify(
            s => s.HasFreshProofAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbid_NonSeedAccount_EvenWhenBypassEnabled()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: true);
        var context = CreateContext(email: "member@example.com", sessionId: "session-1");

        await filter.OnAuthorizationAsync(context);

        AssertMfaRequired(context);
    }

    [Fact]
    public async Task OnAuthorization_ShouldForbid_SeedAccount_InProduction()
    {
        var sessionMfa = new Mock<ISessionMfaVerificationService>();

        var filter = CreateFilter(sessionMfa.Object, bypassEnabled: true, environment: "production");
        var context = CreateContext(email: SeedEmail, sessionId: "session-1");

        await filter.OnAuthorizationAsync(context);

        AssertMfaRequired(context);
    }

    private static void AssertMfaRequired(AuthorizationFilterContext context)
    {
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = result.Value.Should().BeOfType<ApiResponse<object?>>().Subject;
        body.Success.Should().BeFalse();
        body.Error!.Code.Should().Be("MFA_REQUIRED");
    }

    private static RequireMfaAttribute.RequireMfaFilter CreateFilter(
        ISessionMfaVerificationService sessionMfa,
        bool bypassEnabled,
        string environment = "development")
    {
        var values = new Dictionary<string, string?>
        {
            ["ENVIRONMENT"] = environment,
        };
        if (bypassEnabled)
        {
            values["AUTH_SEED_ACCOUNT_BYPASS"] = "true";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new RequireMfaAttribute.RequireMfaFilter(sessionMfa, new SeedAccountBypassPolicy(configuration));
    }

    private static AuthorizationFilterContext CreateContext(
        string email,
        string? sessionId,
        string? authVersion = "5")
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
        if (authVersion != null)
        {
            claims.Add(new Claim(TokenService.AuthVersionClaimType, authVersion));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }
}
