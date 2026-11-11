using System.Security.Claims;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth;
using backend.main.features.auth.sessions;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.app;

using FluentAssertions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

namespace backend.tests.Unit.Application.Security;

public class JwtConfigurationTests
{
    [Fact]
    public void AddJwtAuth_ShouldConfigureTokenValidationParameters_AndRolePolicies()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        var options = optionsMonitor.Get("Bearer");
        var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        options.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        options.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        options.TokenValidationParameters.ValidateIssuerSigningKey.Should().BeTrue();
        options.TokenValidationParameters.RequireExpirationTime.Should().BeTrue();
        options.TokenValidationParameters.RequireSignedTokens.Should().BeTrue();
        options.TokenValidationParameters.ValidIssuer.Should().Be("EventXperience");
        options.TokenValidationParameters.ValidAudience.Should().Be("EventXperienceConsumers");
        options.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));

        var adminPolicy = authorization.GetPolicy(AuthorizationPolicies.AdminOnly);
        var organizerPolicy = authorization.GetPolicy(AuthorizationPolicies.OrganizerOnly);

        adminPolicy.Should().NotBeNull();
        organizerPolicy.Should().NotBeNull();
        adminPolicy!.Requirements.OfType<RolesAuthorizationRequirement>().Single()
            .AllowedRoles.Should().Equal(AuthRoles.Admin);
        organizerPolicy!.Requirements.OfType<RolesAuthorizationRequirement>().Single()
            .AllowedRoles.Should().Equal(AuthRoles.Organizer);
    }

    [Fact]
    public void AddJwtAuth_ShouldRequireAnAuthenticatedBearerUser_ByDefaultAndAsFallback()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());
        var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        foreach (var policy in new[] { authorization.DefaultPolicy, authorization.FallbackPolicy })
        {
            policy.Should().NotBeNull();
            policy!.AuthenticationSchemes.Should().Equal(JwtBearerDefaults.AuthenticationScheme);
            policy.Requirements.Should().ContainSingle()
                .Which.Should().BeOfType<DenyAnonymousAuthorizationRequirement>();
        }
    }

    [Fact]
    public void AddJwtAuth_ShouldRegisterTheUnmatchedRouteResultHandler()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());

        provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>()
            .Should().BeOfType<UnmatchedRouteAuthorizationResultHandler>();
    }

    [Fact]
    public async Task OnChallenge_ShouldReturnUnauthorizedPayload_ForMissingToken()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Response.Body = new MemoryStream();

        var challengeContext = new JwtBearerChallengeContext(
            context,
            CreateScheme(),
            options,
            new AuthenticationProperties());

        await options.Events!.OnChallenge!(challengeContext);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().StartWith("application/json");
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("message").GetString().Should().Be("Missing access token");
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task OnChallenge_ShouldReturnUnauthorizedPayload_ForInvalidToken()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Response.Body = new MemoryStream();

        var challengeContext = new JwtBearerChallengeContext(
            context,
            CreateScheme(),
            options,
            new AuthenticationProperties())
        {
            ErrorDescription = "expired"
        };

        await options.Events!.OnChallenge!(challengeContext);

        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("message").GetString().Should().Be("Invalid or expired access token");
    }

    [Fact]
    public async Task OnForbidden_ShouldReturnForbiddenPayload()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Response.Body = new MemoryStream();

        var forbiddenContext = new ForbiddenContext(context, CreateScheme(), options);

        await options.Events!.OnForbidden!(forbiddenContext);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("message").GetString()
            .Should().Be("You do not have permission to access this resource");
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task OnTokenValidated_ShouldFail_WhenClaimsAreInvalid()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthSessionRepository>());
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };

        var tokenValidatedContext = new TokenValidatedContext(context, CreateScheme(), options)
        {
            Principal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "not-an-int")],
                    "Bearer"))
        };

        await options.Events!.OnTokenValidated!(tokenValidatedContext);

        tokenValidatedContext.Result.Should().NotBeNull();
        tokenValidatedContext.Result!.Failure!.Message.Should().Be("Invalid token payload.");
    }

    [Fact]
    public async Task OnTokenValidated_ShouldFail_WhenTheSessionClaimIsMissing()
    {
        var repository = new Mock<IAuthSessionRepository>();
        using var provider = BuildJwtProvider(repository);
        var tokenValidatedContext = CreateTokenValidatedContext(
            provider,
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(TokenService.AuthVersionClaimType, "1"));

        await InvokeOnTokenValidatedAsync(provider, tokenValidatedContext);

        tokenValidatedContext.Result!.Failure!.Message.Should().Be("Invalid token payload.");
        repository.VerifyNoOtherCalls();
    }

    public static TheoryData<AccessTokenSessionState?> RejectedStates => new()
    {
        null,
        new AccessTokenSessionState(IsDisabled: true, AuthVersion: 1, SessionActive: true),
        new AccessTokenSessionState(IsDisabled: false, AuthVersion: 2, SessionActive: true),
        // Revoked, expired, or never recorded: the registry has no live row for the sid.
        new AccessTokenSessionState(IsDisabled: false, AuthVersion: 1, SessionActive: false),
    };

    [Theory]
    [MemberData(nameof(RejectedStates))]
    public async Task OnTokenValidated_ShouldFail_UnlessTheAccountAndSessionAreLive(
        AccessTokenSessionState? state)
    {
        var repository = new Mock<IAuthSessionRepository>();
        repository.Setup(r => r.GetAccessTokenStateAsync(42, SessionId, It.IsAny<DateTime>()))
            .ReturnsAsync(state);
        using var provider = BuildJwtProvider(repository);
        var tokenValidatedContext = CreateTokenValidatedContext(
            provider,
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(TokenService.AuthVersionClaimType, "1"),
            new Claim(TokenService.SessionIdClaimType, SessionId));

        await InvokeOnTokenValidatedAsync(provider, tokenValidatedContext);

        tokenValidatedContext.Result.Should().NotBeNull();
        tokenValidatedContext.Result!.Failure!.Message.Should().Be("This access token is no longer valid.");
    }

    [Fact]
    public async Task OnTokenValidated_ShouldSucceed_ForALiveSessionOfAnActiveUserWithMatchingAuthVersion()
    {
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var repository = new Mock<IAuthSessionRepository>();
        repository.Setup(r => r.GetAccessTokenStateAsync(42, SessionId, now.UtcDateTime))
            .ReturnsAsync(new AccessTokenSessionState(IsDisabled: false, AuthVersion: 2, SessionActive: true));
        using var provider = BuildJwtProvider(repository, new FixedTimeProvider(now));
        var tokenValidatedContext = CreateTokenValidatedContext(
            provider,
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(TokenService.AuthVersionClaimType, "2"),
            new Claim(TokenService.SessionIdClaimType, SessionId));

        await InvokeOnTokenValidatedAsync(provider, tokenValidatedContext);

        tokenValidatedContext.Result.Should().BeNull();
        repository.Verify(r => r.GetAccessTokenStateAsync(42, SessionId, now.UtcDateTime), Times.Once);
    }

    [Fact]
    public void GetUserPayload_ShouldReturnNormalizedPayload()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "12"),
                    new Claim(ClaimTypes.Name, "person@example.com"),
                    new Claim(ClaimTypes.Role, " organizer ")
                ],
                "Bearer"));

        var payload = principal.GetUserPayload();

        payload.Id.Should().Be(12);
        payload.Email.Should().Be("person@example.com");
        payload.Role.Should().Be(AuthRoles.Organizer);
    }

    [Fact]
    public void GetUserPayload_ShouldThrow_WhenRequiredClaimsAreMissing()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "12")],
                "Bearer"));

        var action = () => principal.GetUserPayload();

        action.Should().Throw<InvalidTokenPayloadException>();
    }

    private const string SessionId = "0123456789abcdef0123456789abcdef";

    private static ServiceProvider BuildJwtProvider(
        Mock<IAuthSessionRepository> repository,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(repository.Object);
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddJwtAuth(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static TokenValidatedContext CreateTokenValidatedContext(
        ServiceProvider provider,
        params Claim[] claims)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };

        return new TokenValidatedContext(context, CreateScheme(), options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
    }

    private static Task InvokeOnTokenValidatedAsync(ServiceProvider provider, TokenValidatedContext context)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        return options.Events!.OnTokenValidated!(context);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AuthenticationScheme CreateScheme()
    {
        return new AuthenticationScheme("Bearer", "Bearer", typeof(JwtBearerHandler));
    }
}
