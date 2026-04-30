using backend.main.configurations.security;
using backend.main.dtos.requests.auth;
using backend.main.dtos.responses.auth;
using backend.main.dtos.responses.general;
using backend.main.implementation.controllers;
using backend.main.models.core;
using backend.main.services.interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace backend.test;

public class UserAdminControllerTests
{
    [Fact]
    public async Task UpdateUserStatus_ReturnsDisabledStatusPayload()
    {
        var userService = new Mock<IUserService>();
        userService.Setup(service => service.UpdateUserStatusAsync(42, true, "Terms violation"))
            .ReturnsAsync(new User
            {
                Id = 42,
                Email = "user@example.com",
                Usertype = AuthRoles.Participant,
                IsDisabled = true,
                DisabledReason = "Terms violation",
                DisabledAtUtc = DateTime.UtcNow,
                AuthVersion = 3,
            });

        var controller = new UserAdminController(userService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.UpdateUserStatus(42, new UpdateUserStatusRequest
        {
            IsDisabled = true,
            Reason = "Terms violation",
        });

        var payload = result.Should().BeOfType<ObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<UserStatusResponse>>()
            .Subject;

        payload.Data.Should().NotBeNull();
        payload.Data!.Id.Should().Be(42);
        payload.Data.IsDisabled.Should().BeTrue();
        payload.Data.DisabledReason.Should().Be("Terms violation");
        payload.Data.AuthVersion.Should().Be(3);
    }
}
