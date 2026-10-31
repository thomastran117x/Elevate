using System.Security.Claims;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.history;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth.History;

public class AuthSecurityHistoryControllerTests
{
    [Fact]
    public async Task List_ShouldReturnTheCallersPage()
    {
        var page = new PagedResponse<AccountSecurityEventResponse>([], 0, 3, 15);
        var service = new Mock<ISecurityHistoryService>();
        service.Setup(s => s.GetPageAsync(7, 3, 15, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await CreateController(service.Object).List(3, 15);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<PagedResponse<AccountSecurityEventResponse>>>()
            .Which.Data.Should().BeSameAs(page);
    }

    [Fact]
    public async Task List_ShouldResolveAppExceptions()
    {
        var service = new Mock<ISecurityHistoryService>();
        service.Setup(s => s.GetPageAsync(7, 1, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("missing"));

        var result = await CreateController(service.Object).List();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task List_ShouldReturn500_ForUnexpectedFailures()
    {
        var service = new Mock<ISecurityHistoryService>();
        service.Setup(s => s.GetPageAsync(7, 1, 20, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateController(service.Object).List();

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task List_ShouldReturnNothing_WhenTheClientAborts()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var service = new Mock<ISecurityHistoryService>();
        service.Setup(s => s.GetPageAsync(7, 1, 20, aborted.Token))
            .ThrowsAsync(new OperationCanceledException(aborted.Token));

        var result = await CreateController(service.Object).List(cancellationToken: aborted.Token);

        result.Should().BeOfType<EmptyResult>();
    }

    private static AuthSecurityHistoryController CreateController(ISecurityHistoryService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "7"),
                        new Claim(ClaimTypes.Name, "owner@example.com"),
                        new Claim(ClaimTypes.Role, "Participant")
                    ], "TestAuth"))
                }
            }
        };
}
