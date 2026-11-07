using System.Security.Claims;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.features.auth.mfa.recovery;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Auth.RecoveryCodes;

public class AuthRecoveryCodesControllerTests
{
    private static readonly DateTime GeneratedAt = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IRecoveryCodeService> _service = new();
    private readonly Mock<IMfaFactorChangeService> _factorChanges = new();

    [Fact]
    public async Task GetStatus_ShouldReturnOnlyTheCountsAndGenerationTime()
    {
        _service.Setup(s => s.GetStatusAsync(42)).ReturnsAsync(new RecoveryCodeStatus(3, GeneratedAt));

        var result = await CreateController().GetStatus();

        var data = Extract<RecoveryCodeStatusResponse>(result, 200).Data!;
        data.RemainingCount.Should().Be(3);
        data.TotalCount.Should().Be(10);
        data.GeneratedAtUtc.Should().Be(GeneratedAt);
    }

    [Theory]
    [InlineData(false, MfaFactorChange.Enabled)]
    [InlineData(true, MfaFactorChange.Regenerated)]
    public async Task Regenerate_ShouldReturnTheCodesOnce_AndRecordTheChange(bool replaced, MfaFactorChange expectedChange)
    {
        var codes = RecoveryCodeFormat.GenerateSet().Select(RecoveryCodeFormat.Display).ToArray();
        _service.Setup(s => s.RegenerateAsync(42)).ReturnsAsync(new RecoveryCodeSet(codes, GeneratedAt, replaced));

        var result = await CreateController().Regenerate();

        var data = Extract<RecoveryCodesResponse>(result, 200).Data!;
        data.Codes.Should().Equal(codes);
        data.GeneratedAtUtc.Should().Be(GeneratedAt);
        _factorChanges.Verify(
            f => f.RecordAsync(42, "member@example.com", MfaFactorKind.RecoveryCodes, expectedChange),
            Times.Once);
    }

    [Fact]
    public async Task Actions_ShouldResolveServiceErrors()
    {
        _service.Setup(s => s.GetStatusAsync(42)).ThrowsAsync(new InternalServerErrorException());
        _service.Setup(s => s.RegenerateAsync(42)).ThrowsAsync(new InvalidOperationException("unexpected"));

        ((ObjectResult)await CreateController().GetStatus()).StatusCode.Should().Be(500);
        ((ObjectResult)await CreateController().Regenerate()).StatusCode.Should().Be(500);
        _factorChanges.VerifyNoOtherCalls();
    }

    private AuthRecoveryCodesController CreateController() =>
        new(_service.Object, _factorChanges.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(ClaimTypes.Name, "member@example.com"),
                        new Claim(ClaimTypes.Role, "Participant"),
                    ], "TestAuth")),
                },
            },
        };

    private static ApiResponse<T> Extract<T>(IActionResult result, int expectedStatusCode)
    {
        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(expectedStatusCode);
        return objectResult.Value.Should().BeOfType<ApiResponse<T>>().Subject;
    }
}
