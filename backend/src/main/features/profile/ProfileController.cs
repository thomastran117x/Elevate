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

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<MyProfileResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyProfile()
        {
            try
            {
                var userPayload = User.GetUserPayload();
                var user = await _userService.GetUserByIdAsync(userPayload.Id);

                return Ok(new ApiResponse<MyProfileResponse>(
                    "Profile fetched successfully.",
                    MapToMyProfile(user)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[ProfileController] GetMyProfile failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("{username}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<PublicProfileResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPublicProfile(string username)
        {
            try
            {
                var profile = await _userService.GetPublicProfileByUsernameAsync(username);

                return Ok(new ApiResponse<PublicProfileResponse>(
                    "Profile fetched successfully.",
                    new PublicProfileResponse
                    {
                        Username = profile.Username,
                        Name = profile.Name,
                        Avatar = profile.Avatar,
                        Usertype = profile.Usertype,
                        CreatedAtUtc = profile.CreatedAtUtc,
                    }
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[ProfileController] GetPublicProfile failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPatch]
        [ProducesResponseType(typeof(ApiResponse<MyProfileResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                var userPayload = User.GetUserPayload();

                var updatedUser = await _userService.UpdateUserAsync(
                    userPayload.Id,
                    new User
                    {
                        // Email/Usertype are required by the User type but are intentionally
                        // ignored by UpdatePartialAsync — they are never persisted from here.
                        Id = userPayload.Id,
                        Email = userPayload.Email,
                        Usertype = userPayload.Role,
                        Name = request.Name,
                        Username = request.Username,
                        Phone = request.Phone,
                        Address = request.Address,
                    }
                );

                if (updatedUser == null)
                    throw new ResourceNotFoundException("User not found.");

                return Ok(new ApiResponse<MyProfileResponse>(
                    "Profile updated successfully.",
                    MapToMyProfile(updatedUser)
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

        [HttpPost("avatar")]
        [ProducesResponseType(typeof(ApiResponse<MyProfileResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadAvatar([FromForm] AvatarUploadRequest request)
        {
            try
            {
                var userPayload = User.GetUserPayload();
                var updatedUser = await _userService.UpdateAvatarAsync(userPayload.Id, request.Image);

                if (updatedUser == null)
                    throw new ResourceNotFoundException("User not found.");

                return Ok(new ApiResponse<MyProfileResponse>(
                    "Avatar updated successfully.",
                    MapToMyProfile(updatedUser)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[ProfileController] UploadAvatar failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("change-password")]
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

        private static MyProfileResponse MapToMyProfile(User user)
        {
            return new MyProfileResponse
            {
                Id = user.Id,
                Email = user.Email,
                Username = user.Username ?? string.Empty,
                Name = user.Name,
                Avatar = user.Avatar,
                Usertype = user.Usertype,
                Phone = user.Phone,
                Address = user.Address,
                GoogleLinked = !string.IsNullOrEmpty(user.GoogleID),
                MicrosoftLinked = !string.IsNullOrEmpty(user.MicrosoftID),
                CreatedAtUtc = user.CreatedAt,
                UpdatedAtUtc = user.UpdatedAt,
            };
        }
    }
}
