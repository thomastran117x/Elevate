using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

using backend.main.features.auth.oauth;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace backend.tests.Unit.Features.Auth;

public class OAuthServiceTests
{
    [Fact]
    public async Task ExchangeGoogleCodeAsync_ShouldThrowNotAvailable_WhenGoogleOAuthIsNotConfigured()
    {
        var service = CreateService();
        SetPrivateField(service, "_googleClientId", null);
        SetPrivateField(service, "_googleClientSecret", null);

        var action = () => service.ExchangeGoogleCodeAsync("code", "verifier", "https://app.example.com/callback");

        await action.Should()
            .ThrowAsync<NotAvailableException>()
            .WithMessage("Google OAuth is not available");
    }

    [Fact]
    public async Task ExchangeGoogleCodeAsync_ShouldThrowUnauthorized_WhenExchangeFails()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad_code")
        });
        SetPrivateField(service, "_googleClientId", "google-client");
        SetPrivateField(service, "_googleClientSecret", "google-secret");

        var action = () => service.ExchangeGoogleCodeAsync("bad-code", "verifier", "https://app.example.com/callback");

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("*bad_code*");
    }

    [Fact]
    public async Task ExchangeGoogleCodeAsync_ShouldThrowUnauthorized_WhenIdTokenIsMissing()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id_token = "" })
        });
        SetPrivateField(service, "_googleClientId", "google-client");
        SetPrivateField(service, "_googleClientSecret", "google-secret");

        var action = () => service.ExchangeGoogleCodeAsync("code", "verifier", "https://app.example.com/callback");

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("No id_token returned from Google.");
    }

    [Fact]
    public async Task ExchangeGoogleCodeAsync_ShouldReturnIdToken_WhenExchangeSucceeds()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id_token = "google-id-token" })
        });
        SetPrivateField(service, "_googleClientId", "google-client");
        SetPrivateField(service, "_googleClientSecret", "google-secret");

        var token = await service.ExchangeGoogleCodeAsync("code", "verifier", "https://app.example.com/callback");

        token.Should().Be("google-id-token");
    }

    [Fact]
    public async Task VerifyGoogleTokenAsync_ShouldThrowNotAvailable_WhenGoogleOAuthIsNotConfigured()
    {
        var service = CreateService();
        SetPrivateField(service, "_googleClientId", null);

        var action = () => service.VerifyGoogleTokenAsync("token");

        await action.Should()
            .ThrowAsync<NotAvailableException>()
            .WithMessage("Google OAuth is not available");
    }

    [Fact]
    public async Task VerifyAppleTokenAsync_ShouldReturnOAuthUser_ForValidToken()
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var service = CreateService();
        SetPrivateField(service, "_appleClientId", "apple-client");
        SetPrivateField(service, "_appleConfigManager", CreateConfigManager(key, "https://appleid.apple.com"));

        var token = CreateJwt(
            issuer: "https://appleid.apple.com",
            audience: "apple-client",
            key: key,
            claims:
            [
                new Claim("sub", "apple-user-1"),
                new Claim("email", "apple@example.com")
            ]);

        var result = await service.VerifyAppleTokenAsync(token);

        result.Id.Should().Be("apple-user-1");
        result.Email.Should().Be("apple@example.com");
        result.Name.Should().Be("apple@example.com");
        result.Provider.Should().Be("apple");
    }

    [Fact]
    public async Task VerifyAppleTokenAsync_ShouldThrowUnauthorized_WhenRequiredClaimsAreMissing()
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var service = CreateService();
        SetPrivateField(service, "_appleClientId", "apple-client");
        SetPrivateField(service, "_appleConfigManager", CreateConfigManager(key, "https://appleid.apple.com"));

        var token = CreateJwt(
            issuer: "https://appleid.apple.com",
            audience: "apple-client",
            key: key,
            claims:
            [
                new Claim("sub", "apple-user-1")
            ]);

        var action = () => service.VerifyAppleTokenAsync(token);

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Missing Apple email claim");
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldReturnOAuthUser_ForValidToken()
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var service = CreateService();
        SetPrivateField(service, "_microsoftClientId", "ms-client");
        SetPrivateField(service, "_microsoftConfigManager", CreateConfigManager(key, "https://login.microsoftonline.com/test-tenant/v2.0"));

        var token = CreateJwt(
            issuer: "https://login.microsoftonline.com/test-tenant/v2.0",
            audience: "ms-client",
            key: key,
            claims:
            [
                new Claim("sub", "microsoft-user-1"),
                new Claim("preferred_username", "microsoft@example.com"),
                new Claim("name", "Microsoft User"),
                new Claim("nonce", "expected-nonce")
            ]);

        var result = await service.VerifyMicrosoftTokenAsync(token, "expected-nonce");

        result.Id.Should().Be("microsoft-user-1");
        result.Email.Should().Be("microsoft@example.com");
        result.Name.Should().Be("Microsoft User");
        result.Provider.Should().Be("microsoft");
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldThrowUnauthorized_WhenNonceDoesNotMatch()
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var service = CreateService();
        SetPrivateField(service, "_microsoftClientId", "ms-client");
        SetPrivateField(service, "_microsoftConfigManager", CreateConfigManager(key, "https://login.microsoftonline.com/test-tenant/v2.0"));

        var token = CreateJwt(
            issuer: "https://login.microsoftonline.com/test-tenant/v2.0",
            audience: "ms-client",
            key: key,
            claims:
            [
                new Claim("sub", "microsoft-user-1"),
                new Claim("preferred_username", "microsoft@example.com"),
                new Claim("nonce", "actual-nonce")
            ]);

        var action = () => service.VerifyMicrosoftTokenAsync(token, "expected-nonce");

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Microsoft nonce validation failed");
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldThrowUnauthorized_WhenEmailClaimIsMissing()
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var service = CreateService();
        SetPrivateField(service, "_microsoftClientId", "ms-client");
        SetPrivateField(service, "_microsoftConfigManager", CreateConfigManager(key, "https://login.microsoftonline.com/test-tenant/v2.0"));

        var token = CreateJwt(
            issuer: "https://login.microsoftonline.com/test-tenant/v2.0",
            audience: "ms-client",
            key: key,
            claims:
            [
                new Claim("sub", "microsoft-user-1")
            ]);

        var action = () => service.VerifyMicrosoftTokenAsync(token);

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Missing Microsoft email claim");
    }

    private static OAuthService CreateService(Func<HttpRequestMessage, HttpResponseMessage>? responseFactory = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://unit.test/")
        };

        return new OAuthService(httpClient);
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigManager(SecurityKey signingKey, string issuer)
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = issuer
        };
        configuration.SigningKeys.Add(signingKey);

        return new ConfigurationManager<OpenIdConnectConfiguration>(
            issuer,
            new StaticConfigurationRetriever(configuration),
            new StaticDocumentRetriever());
    }

    private static string CreateJwt(string issuer, string audience, SecurityKey key, IEnumerable<Claim> claims)
    {
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(target, value);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? responseFactory)
        {
            _responseFactory = responseFactory
                ?? DefaultResponseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }

        private static HttpResponseMessage DefaultResponseFactory(HttpRequestMessage _)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id_token = "default-token" })
            };
        }
    }

    private sealed class StaticConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration;

        public StaticConfigurationRetriever(OpenIdConnectConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(
            string address,
            IDocumentRetriever retriever,
            CancellationToken cancel)
        {
            return Task.FromResult(_configuration);
        }
    }

    private sealed class StaticDocumentRetriever : IDocumentRetriever
    {
        public bool RequireHttps { get; set; } = false;

        public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            return Task.FromResult("{}");
        }
    }
}
