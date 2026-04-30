using backend.main.dtos.requests.auth;
using backend.main.dtos.responses.auth;
using backend.main.dtos.responses.general;
using backend.main.models.core;
using backend.main.services.interfaces;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("admin/users")]
    [Authorize("AdminOnly")]
    public class UserAdminController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserAdminController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPatch("{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusRequest request)
        {
            User user = await _userService.UpdateUserStatusAsync(id, request.IsDisabled, request.Reason);

            return StatusCode(
                200,
                new ApiResponse<UserStatusResponse>(
                    request.IsDisabled
                        ? "User disabled successfully."
                        : "User re-enabled successfully.",
                    new UserStatusResponse
                    {
                        Id = user.Id,
                        IsDisabled = user.IsDisabled,
                        DisabledAtUtc = user.DisabledAtUtc,
                        DisabledReason = user.DisabledReason,
                        AuthVersion = user.AuthVersion,
                    }
                )
            );
        }
    }
}
