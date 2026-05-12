using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.dtos.responses.general;
using backend.main.features.profile;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.profile.admin
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
            var user = await _userService.UpdateUserStatusAsync(id, request.IsDisabled, request.Reason);

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
