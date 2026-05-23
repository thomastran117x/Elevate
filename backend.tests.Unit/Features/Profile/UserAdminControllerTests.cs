using backend.main.features.auth.contracts;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.profile;
using backend.main.features.profile.admin;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Profile;

public class UserAdminControllerTests
{
    [Fact]
    public async Task UpdateUserStatus_ShouldReturnDisablePayload()
    {
        var service = new Mock<IUserService>();
        service.Setup(s => s.UpdateUserStatusAsync(9, true, "abuse"))
            .ReturnsAsync(new UserStatusRecord
            {
                Id = 9,
                IsDisabled = true,
                DisabledReason = "abuse",
                DisabledAtUtc = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc),
                AuthVersion = 3
            });

        var controller = new UserAdminController(service.Object);

        var result = await controller.UpdateUserStatus(9, new UpdateUserStatusRequest
        {
            IsDisabled = true,
            Reason = "abuse"
        });

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<UserStatusResponse>>().Subject;
        response.Message.Should().Be("User disabled successfully.");
        response.Data!.IsDisabled.Should().BeTrue();
        response.Data.DisabledReason.Should().Be("abuse");
    }

    [Fact]
    public async Task UpdateUserStatus_ShouldReturnReEnablePayload()
    {
        var service = new Mock<IUserService>();
        service.Setup(s => s.UpdateUserStatusAsync(9, false, null))
            .ReturnsAsync(new UserStatusRecord
            {
                Id = 9,
                IsDisabled = false,
                AuthVersion = 4
            });

        var controller = new UserAdminController(service.Object);

        var result = await controller.UpdateUserStatus(9, new UpdateUserStatusRequest
        {
            IsDisabled = false
        });

        var ok = result.Should().BeOfType<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<UserStatusResponse>>().Subject;
        response.Message.Should().Be("User re-enabled successfully.");
        response.Data!.IsDisabled.Should().BeFalse();
    }
}
