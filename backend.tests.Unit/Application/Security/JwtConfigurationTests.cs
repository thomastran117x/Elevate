using System.Security.Claims;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth;
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
        using var provider = BuildJwtProvider(new Mock<IAuthUserRepository>());
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

        var adminPolicy = authorization.GetPolicy("AdminOnly");
        var organizerPolicy = authorization.GetPolicy("OrganizerOnly");

        adminPolicy.Should().NotBeNull();
        organizerPolicy.Should().NotBeNull();
        adminPolicy!.Requirements.OfType<RolesAuthorizationRequirement>().Single()
            .AllowedRoles.Should().Equal(AuthRoles.Admin);
        organizerPolicy!.Requirements.OfType<RolesAuthorizationRequirement>().Single()
            .AllowedRoles.Should().Equal(AuthRoles.Organizer);
    }

    [Fact]
    public async Task OnChallenge_ShouldReturnUnauthorizedPayload_ForMissingToken()
    {
        using var provider = BuildJwtProvider(new Mock<IAuthUserRepository>());
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
        using var provider = BuildJwtProvider(new Mock<IAuthUserRepository>());
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
        using var provider = BuildJwtProvider(new Mock<IAuthUserRepository>());
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
        using var provider = BuildJwtProvider(new Mock<IAuthUserRepository>());
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
    public async Task OnTokenValidated_ShouldFail_WhenUserIsDisabledOrAuthVersionMismatch()
    {
        var repository = new Mock<IAuthUserRepository>();
        repository.Setup(service => service.GetUserAsync(42))
            .ReturnsAsync(new User
            {
                Id = 42,
                Email = "user@example.com",
                Usertype = AuthRoles.Participant,
                IsDisabled = true,
                AuthVersion = 1
            });

        using var provider = BuildJwtProvider(repository);
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };

        var tokenValidatedContext = new TokenValidatedContext(context, CreateScheme(), options)
        {
            Principal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(TokenService.AuthVersionClaimType, "1")
                    ],
                    "Bearer"))
        };

        await options.Events!.OnTokenValidated!(tokenValidatedContext);

        tokenValidatedContext.Result.Should().NotBeNull();
        tokenValidatedContext.Result!.Failure!.Message.Should().Be("This access token is no longer valid.");
    }

    [Fact]
    public async Task OnTokenValidated_ShouldSucceed_ForActiveUserWithMatchingAuthVersion()
    {
        var repository = new Mock<IAuthUserRepository>();
        repository.Setup(service => service.GetUserAsync(42))
            .ReturnsAsync(new User
            {
                Id = 42,
                Email = "user@example.com",
                Usertype = AuthRoles.Organizer,
                IsDisabled = false,
                AuthVersion = 2
            });

        using var provider = BuildJwtProvider(repository);
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };

        var tokenValidatedContext = new TokenValidatedContext(context, CreateScheme(), options)
        {
            Principal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(TokenService.AuthVersionClaimType, "2")
                    ],
                    "Bearer"))
        };

        await options.Events!.OnTokenValidated!(tokenValidatedContext);

        tokenValidatedContext.Result.Should().BeNull();
        repository.Verify(service => service.GetUserAsync(42), Times.Once);
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

    private static ServiceProvider BuildJwtProvider(Mock<IAuthUserRepository> repository)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(repository.Object);
        services.AddJwtAuth(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static AuthenticationScheme CreateScheme()
    {
        return new AuthenticationScheme("Bearer", "Bearer", typeof(JwtBearerHandler));
    }
}
