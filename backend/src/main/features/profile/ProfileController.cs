using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth;
using backend.main.features.auth.token;
using backend.main.features.profile.contracts.requests;
using backend.main.features.profile.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.profile
{
    [ApiController]
    [FeatureGate(FeatureFlagKeys.Profile)]
    [Route("profile")]
    [Authorize]
    public class ProfileController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAuthService _authService;
        private readonly ITokenService _tokenService;

        public ProfileController(
            IUserService userService,
            IAuthService authService,
            ITokenService tokenService
        )
        {
            _userService = userService;
            _authService = authService;
            _tokenService = tokenService;
        }

        [HttpPatch]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(typeof(ApiResponse<ProfileResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                var userPayload = User.GetUserPayload();

                var updatedUser = await _userService.UpdateUserAsync(
                    userPayload.Id,
                    new User
                    {
                        Id = userPayload.Id,
                        Email = userPayload.Email,
                        Usertype = userPayload.Role,
                        Name = request.Name,
                        Username = request.Username,
                        Avatar = request.Avatar,
                    }
                );

                if (updatedUser == null)
                    throw new ResourceNotFoundException("User not found.");

                return Ok(new ApiResponse<ProfileResponse>(
                    "Profile updated successfully.",
                    new ProfileResponse
                    {
                        Id = updatedUser.Id,
                        Email = updatedUser.Email,
                        Username = updatedUser.Username,
                        Name = updatedUser.Name,
                        Avatar = updatedUser.Avatar,
                        Usertype = updatedUser.Usertype,
                    }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[ProfileController] UpdateProfile failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("change-password")]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordAuthenticatedRequest request)
        {
            try
            {
                var userPayload = User.GetUserPayload();
                await _authService.ChangePasswordForAuthenticatedUserAsync(
                    userPayload.Email,
                    request.CurrentPassword,
                    request.NewPassword
                );

                return Ok(new MessageResponse("Password changed successfully. Please sign in again."));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[ProfileController] ChangePassword failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpDelete]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteAccount()
        {
            try
            {
                var userPayload = User.GetUserPayload();

                await _tokenService.RevokeAllRefreshSessionsAsync(userPayload.Id);
                await _userService.DeleteUserAsync(userPayload.Id);

                HttpUtility.ClearBrowserRefreshSession(Response);

                return Ok(new MessageResponse("Account deleted successfully."));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[ProfileController] DeleteAccount failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
