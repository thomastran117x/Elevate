using System.Security.Claims;

using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.oauth.links;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth.ProviderLinks;

public class AuthProviderLinksControllerTests
{
    private static readonly DateTime LinkedAt = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
    private readonly Mock<IProviderLinkService> _service = new();

    [Fact]
    public async Task List_ShouldReturnEachProvider_WithoutTheSubject()
    {
        _service.Setup(s => s.GetOverviewAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new ProviderLinkOverview(
            HasPassword: true,
            [
                new ProviderLinkStatus(IdentityProviders.Google, true, Link(), CanUnlink: true),
                new ProviderLinkStatus(IdentityProviders.Microsoft, false, null, CanUnlink: false),
            ]));

        var data = Extract<ProviderLinksResponse>(await CreateController().List(CancellationToken.None), 200).Data!;

        data.HasPassword.Should().BeTrue();
        data.Providers.Should().HaveCount(2);
        var google = data.Providers[0];
        google.Provider.Should().Be(IdentityProviders.Google);
        google.Available.Should().BeTrue();
        google.CanUnlink.Should().BeTrue();
        google.Link!.Email.Should().Be("owner@gmail.com");
        google.Link.LinkedAtUtc.Should().Be(LinkedAt);
        typeof(ProviderLinkResponse).GetProperties().Select(p => p.Name).Should().NotContain("Subject");
        data.Providers[1].Link.Should().BeNull();
    }

    [Fact]
    public async Task Start_ShouldPassTheAccessTokensSession()
    {
        _service.Setup(s => s.StartAsync(new ProviderLinkSession(42, "session-42", 5), "microsoft", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderLinkStart("https://login.example/authorize", "state", "https://app.example/auth/microsoft", "verifier"));

        var data = Extract<ProviderLinkStartResponse>(await CreateController().Start("microsoft", CancellationToken.None), 200).Data!;

        data.AuthorizationUrl.Should().Be("https://login.example/authorize");
        data.State.Should().Be("state");
        data.RedirectUri.Should().Be("https://app.example/auth/microsoft");
        data.CodeVerifier.Should().Be("verifier");
    }

    [Fact]
    public async Task Start_ShouldRefuse_ATokenWithoutASession()
    {
        var result = await CreateController(withSession: false).Start("google", CancellationToken.None);

        ((ObjectResult)result).StatusCode.Should().Be(401);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Complete_ShouldReturnTheNewLink()
    {
        _service.Setup(s => s.CompleteAsync(new ProviderLinkSession(42, "session-42", 5), "google", "state", "code", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Link());

        var result = await CreateController().Complete("google", new CompleteProviderLinkRequest
        {
            State = "state",
            Code = "code",
        }, CancellationToken.None);

        Extract<ProviderLinkResponse>(result, 200).Data!.Provider.Should().Be(IdentityProviders.Google);
    }

    [Fact]
    public async Task Confirm_ShouldConfirmForTheCaller()
    {
        _service.Setup(s => s.ConfirmEmailLinkAsync(42, "link-token", It.IsAny<CancellationToken>())).ReturnsAsync(Link());

        var result = await CreateController().Confirm(new ConfirmProviderLinkRequest { LinkToken = "link-token" }, CancellationToken.None);

        Extract<ProviderLinkResponse>(result, 200).Data!.DisplayName.Should().Be("Owner");
    }

    [Fact]
    public async Task Unlink_ShouldUnlinkForTheCaller()
    {
        var result = await CreateController().Unlink("google", CancellationToken.None);

        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(200);
        _service.Verify(s => s.UnlinkAsync(42, "google", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Actions_ShouldResolveServiceErrors_WithTheirStatus()
    {
        _service.Setup(s => s.GetOverviewAsync(42, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("unexpected"));
        _service.Setup(s => s.StartAsync(It.IsAny<ProviderLinkSession>(), "google", It.IsAny<CancellationToken>()))
            .ThrowsAsync(ProviderLinkConflictException.AlreadyConnected());
        _service.Setup(s => s.CompleteAsync(It.IsAny<ProviderLinkSession>(), "google", "state", null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderLinkExpiredException());
        _service.Setup(s => s.ConfirmEmailLinkAsync(42, "link-token", It.IsAny<CancellationToken>())).ThrowsAsync(new ProviderLinkMismatchException());
        _service.Setup(s => s.UnlinkAsync(42, "google", It.IsAny<CancellationToken>())).ThrowsAsync(ProviderLinkConflictException.LastSignInMethod());
        var controller = CreateController();

        ((ObjectResult)await controller.List(CancellationToken.None)).StatusCode.Should().Be(500);
        ((ObjectResult)await controller.Start("google", CancellationToken.None)).StatusCode.Should().Be(409);
        ((ObjectResult)await controller.Complete("google", new CompleteProviderLinkRequest { State = "state" }, CancellationToken.None))
            .StatusCode.Should().Be(410);
        ((ObjectResult)await controller.Confirm(new ConfirmProviderLinkRequest { LinkToken = "link-token" }, CancellationToken.None))
            .StatusCode.Should().Be(403);
        ((ObjectResult)await controller.Unlink("google", CancellationToken.None)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Actions_ShouldResolveUnexpectedFailures_AsServerErrors()
    {
        _service.Setup(s => s.StartAsync(It.IsAny<ProviderLinkSession>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        _service.Setup(s => s.CompleteAsync(It.IsAny<ProviderLinkSession>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        _service.Setup(s => s.ConfirmEmailLinkAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        _service.Setup(s => s.UnlinkAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected"));
        var controller = CreateController();

        ((ObjectResult)await controller.Start("google", CancellationToken.None)).StatusCode.Should().Be(500);
        ((ObjectResult)await controller.Complete("google", new CompleteProviderLinkRequest { State = "s" }, CancellationToken.None)).StatusCode.Should().Be(500);
        ((ObjectResult)await controller.Confirm(new ConfirmProviderLinkRequest { LinkToken = "t" }, CancellationToken.None)).StatusCode.Should().Be(500);
        ((ObjectResult)await controller.Unlink("google", CancellationToken.None)).StatusCode.Should().Be(500);
    }

    private static UserIdentityLink Link() => new()
    {
        Id = 1,
        UserId = 42,
        Provider = IdentityProviders.Google,
        Subject = "google-subject",
        Email = "owner@gmail.com",
        DisplayName = "Owner",
        LinkedAtUtc = LinkedAt,
    };

    private AuthProviderLinksController CreateController(bool withSession = true)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "42"),
            new(ClaimTypes.Name, "member@example.com"),
            new(ClaimTypes.Role, "Participant"),
        };
        if (withSession)
        {
            claims.Add(new Claim(TokenService.SessionIdClaimType, "session-42"));
            claims.Add(new Claim(TokenService.AuthVersionClaimType, "5"));
        }

        return new AuthProviderLinksController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
    }

    private static ApiResponse<T> Extract<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
