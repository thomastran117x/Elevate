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
            Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"provider detail\"}")
        });
        SetPrivateField(service, "_googleClientId", "google-client");
        SetPrivateField(service, "_googleClientSecret", "google-secret");

        var action = () => service.ExchangeGoogleCodeAsync("bad-code", "verifier", "https://app.example.com/callback");

        // Google's error body is provider content and is not echoed back to the client.
        var thrown = await action.Should().ThrowAsync<UnauthorizedException>();
        thrown.Which.Message.Should().Be("Google code exchange failed.");
        thrown.Which.Message.Should().NotContain("invalid_grant").And.NotContain("provider detail");
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

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ExchangeGoogleCodeAsync_ShouldReportGoogleUnavailable_NotARejectedGrant(HttpStatusCode status)
    {
        var service = CreateService(_ => new HttpResponseMessage(status));
        SetPrivateField(service, "_googleClientId", "google-client");
        SetPrivateField(service, "_googleClientSecret", "google-secret");

        var action = () => service.ExchangeGoogleCodeAsync("code", "verifier", "https://app.example.com/callback");

        // An outage must not be recorded as a rejected provider credential.
        await action.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task ExchangeGoogleCodeAsync_ShouldReportGoogleUnavailable_WhenTheRequestFails()
    {
        var service = CreateService(_ => throw new HttpRequestException("connection refused"));
        SetPrivateField(service, "_googleClientId", "google-client");
        SetPrivateField(service, "_googleClientSecret", "google-secret");

        var action = () => service.ExchangeGoogleCodeAsync("code", "verifier", "https://app.example.com/callback");

        await action.Should().ThrowAsync<NotAvailableException>();
    }

    [Fact]
    public async Task ExchangeGoogleCodeAsync_ShouldThrowUnauthorized_WhenTheResponseHasNoIdTokenProperty()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { access_token = "only-an-access-token" })
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
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("email", "microsoft@example.com"),
            new Claim("name", "Microsoft User"),
            new Claim("nonce", "expected-nonce"));

        var result = await service.VerifyMicrosoftTokenAsync(token, "expected-nonce");

        result.Id.Should().Be("microsoft-user-1");
        result.Email.Should().Be("microsoft@example.com");
        result.Name.Should().Be("Microsoft User");
        result.Provider.Should().Be("microsoft");
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldThrowUnauthorized_WhenNonceDoesNotMatch()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("email", "microsoft@example.com"),
            new Claim("nonce", "actual-nonce"));

        var action = () => service.VerifyMicrosoftTokenAsync(token, "expected-nonce");

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Microsoft nonce validation failed");
    }

    /// <summary>
    /// <c>preferred_username</c> is a UPN a tenant administrator sets, so it is never read as an
    /// email address, even when it is the only address-like claim.
    /// </summary>
    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldReturnNoEmail_WhenOnlyPreferredUsernameIsPresent()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("preferred_username", "victim@example.com"),
            new Claim("tid", MicrosoftConsumerTenantId));

        var result = await service.VerifyMicrosoftTokenAsync(token);

        result.Email.Should().BeNull();
        result.EmailVerified.Should().BeFalse();
        result.Name.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldReadEmailClaim_RatherThanPreferredUsername()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("preferred_username", "victim@example.com"),
            new Claim("email", "holder@example.com"));

        var result = await service.VerifyMicrosoftTokenAsync(token);

        result.Email.Should().Be("holder@example.com");
        result.Name.Should().Be("holder@example.com");
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldReportEmailUnverified_ForWorkTenantWithoutDomainOwnerClaim()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("email", "worker@example.com"),
            new Claim("tid", WorkTenantId));

        var result = await service.VerifyMicrosoftTokenAsync(token);

        result.Email.Should().Be("worker@example.com");
        result.EmailVerified.Should().BeFalse();
    }

    [Theory]
    [InlineData("true", ClaimValueTypes.Boolean, true)]
    [InlineData("1", ClaimValueTypes.String, true)]
    [InlineData("false", ClaimValueTypes.Boolean, false)]
    public async Task VerifyMicrosoftTokenAsync_ShouldTrustDomainOwnerClaim_ForWorkTenant(
        string value,
        string valueType,
        bool expectedVerified)
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("email", "worker@example.com"),
            new Claim("tid", WorkTenantId),
            new Claim("xms_edov", value, valueType));

        var result = await service.VerifyMicrosoftTokenAsync(token);

        result.EmailVerified.Should().Be(expectedVerified);
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldReportEmailVerified_ForConsumerTenant()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"),
            new Claim("email", "personal@example.com"),
            new Claim("tid", MicrosoftConsumerTenantId));

        var result = await service.VerifyMicrosoftTokenAsync(token);

        result.Email.Should().Be("personal@example.com");
        result.EmailVerified.Should().BeTrue();
    }

    /// <summary>
    /// A token without an address is not refused here. A subject that is already linked still
    /// signs in; resolution refuses it only when it would need the address.
    /// </summary>
    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldReturnNoEmail_WhenEmailClaimIsMissing()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("sub", "microsoft-user-1"));

        var result = await service.VerifyMicrosoftTokenAsync(token);

        result.Id.Should().Be("microsoft-user-1");
        result.Email.Should().BeNull();
        result.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldThrowUnauthorized_WhenSubClaimIsMissing()
    {
        var (service, key) = CreateMicrosoftService();

        var token = CreateMicrosoftJwt(
            key,
            new Claim("email", "microsoft@example.com"));

        var action = () => service.VerifyMicrosoftTokenAsync(token);

        await action.Should()
            .ThrowAsync<UnauthorizedException>()
            .WithMessage("Missing Microsoft sub claim");
    }

    [Fact]
    public async Task VerifyGoogleTokenAsync_ShouldRejectAMalformedToken_PromptlyAndAsUnauthorized()
    {
        var service = CreateService();
        SetPrivateField(service, "_googleClientId", "google-client");
        var started = System.Diagnostics.Stopwatch.StartNew();

        var action = () => service.VerifyGoogleTokenAsync("not-a-jwt");

        // Validation fails the same way every time, so it is not retried with backoff.
        var thrown = await action.Should().ThrowAsync<UnauthorizedException>();
        thrown.Which.Message.Should().Be("Invalid Google token.");
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldRejectAWronglySignedToken_AsUnauthorized()
    {
        var (service, _) = CreateMicrosoftService();
        var forged = CreateMicrosoftJwt(
            new SymmetricSecurityKey(Enumerable.Repeat((byte)7, 32).ToArray()),
            new Claim("sub", "microsoft-user-1"));

        var action = () => service.VerifyMicrosoftTokenAsync(forged);

        var thrown = await action.Should().ThrowAsync<UnauthorizedException>();
        thrown.Which.Message.Should().Be("Invalid Microsoft token.");
    }

    [Fact]
    public async Task VerifyMicrosoftTokenAsync_ShouldRejectAnUntrustedIssuer_AsUnauthorized()
    {
        var (service, key) = CreateMicrosoftService();
        var token = CreateJwt("https://evil.example.com/v2.0", "ms-client", key, [new Claim("sub", "microsoft-user-1")]);

        var action = () => service.VerifyMicrosoftTokenAsync(token);

        await action.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid Microsoft token.");
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    public async Task VerifyMicrosoftTokenAsync_ShouldRejectAMalformedToken_AsUnauthorized(string token)
    {
        var (service, _) = CreateMicrosoftService();

        var action = () => service.VerifyMicrosoftTokenAsync(token);

        await action.Should().ThrowAsync<UnauthorizedException>().WithMessage("Invalid Microsoft token.");
    }

    private const string MicrosoftConsumerTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
    private const string WorkTenantId = "11111111-2222-3333-4444-555555555555";
    private const string MicrosoftIssuer = "https://login.microsoftonline.com/test-tenant/v2.0";

    private static (OAuthService Service, SecurityKey Key) CreateMicrosoftService()
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var service = CreateService();
        SetPrivateField(service, "_microsoftClientId", "ms-client");
        SetPrivateField(service, "_microsoftConfigManager", CreateConfigManager(key, MicrosoftIssuer));
        return (service, key);
    }

    private static string CreateMicrosoftJwt(SecurityKey key, params Claim[] claims)
        => CreateJwt(MicrosoftIssuer, "ms-client", key, claims);

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
