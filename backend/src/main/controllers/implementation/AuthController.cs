using backend.main.dtos.requests.auth;
using backend.main.dtos.responses.auth;
using backend.main.dtos.responses.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IAntiforgery _antiforgery;

        public AuthController(IAuthService authService, IAntiforgery antiforgery)
        {
            _authService = authService;
            _antiforgery = antiforgery;
        }

        [HttpPost("login")]
        public async Task<IActionResult> LocalAuthenticate([FromBody] LoginRequest request)
        {
            try
            {
                UserToken userToken = await _authService.LoginAsync(request.Email, request.Password);

                User user = userToken.user;
                Token token = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, token.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    token.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Login successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] LocalAuthenticate failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("signup")]
        public async Task<IActionResult> LocalSignup([FromBody] SignUpRequest request)
        {
            try
            {
                await _authService.SignUpAsync(request.Email, request.Password, request.Usertype);

                return StatusCode(
                    200,
                    new MessageResponse("Verification email sent.")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] LocalSignup failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("verify")]
        public async Task<IActionResult> LocalVerify([FromQuery] string token)
        {
            try
            {
                UserToken userToken = await _authService.VerifyAsync(token);
                User user = userToken.user;
                Token authToken = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, authToken.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    authToken.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Verification successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] LocalVerify failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("google")]
        public async Task<IActionResult> GoogleAuthenticate([FromBody] GoogleRequest request)
        {
            try
            {
                UserToken userToken = await _authService.GoogleAsync(request.Token);

                User user = userToken.user;
                Token token = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, token.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    token.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Login successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] GoogleAuthenticate failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("microsoft")]
        public async Task<IActionResult> MicrosoftAuthenticate([FromBody] MicrosoftRequest request)
        {
            try
            {
                UserToken userToken = await _authService.MicrosoftAsync(request.Token);

                User user = userToken.user;
                Token token = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, token.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    token.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Login successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] ChangePassword failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("refresh")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Refresh()
        {
            try
            {
                string? refreshToken = Request.Cookies["refreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                    throw new UnauthorizedException("Missing refresh token");

                UserToken userToken = await _authService.HandleTokensAsync(refreshToken);

                User user = userToken.user;
                Token token = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, token.RefreshToken);

                return Ok(new AuthResponse(user.Id, user.Email, user.Usertype, token.AccessToken));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] Refresh failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("csrf")]
        public IActionResult Csrf()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            return Ok(new
            {
                token = tokens.RequestToken
            });
        }

        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            try
            {
                string? refreshToken = Request.Cookies["refreshToken"];
                if (string.IsNullOrEmpty(refreshToken))
                    return StatusCode(
                        200,
                        new MessageResponse($"The user is already logged out.")
                    );

                await _authService.HandleLogoutAsync(refreshToken);

                return StatusCode(
                    200,
                    new MessageResponse($"The user's logout is successful")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] Logout failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("device/verify")]
        public async Task<IActionResult> VerifyDevice([FromQuery] string token)
        {
            try
            {
                UserToken userToken = await _authService.VerifyDeviceLoginAsync(token);
                User user = userToken.user;
                Token authToken = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, authToken.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    authToken.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        "Device verified. Login successful.",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] VerifyDevice failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                await _authService.ForgotPasswordAsync(request.Email);

                return StatusCode(
                    200,
                    new MessageResponse("If the account exist, we send a reset email")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] ForgotPassword failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, [FromQuery] string token)
        {
            try
            {
                await _authService.ChangePasswordAsync(token, request.Password);

                return StatusCode(
                    200,
                    new MessageResponse("Password reset successful. Please login")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] ChangePassword failed: {e}");
                return HandleError.Resolve(e);
            }
        }

    }
}
