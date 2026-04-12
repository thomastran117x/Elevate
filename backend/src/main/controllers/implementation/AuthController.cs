using backend.main.dtos.requests.auth;
using backend.main.dtos.general;
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
        private readonly ClientRequestInfo _requestInfo;

        public AuthController(
            IAuthService authService,
            IAntiforgery antiforgery,
            ClientRequestInfo requestInfo
        )
        {
            _authService = authService;
            _antiforgery = antiforgery;
            _requestInfo = requestInfo;
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LocalAuthenticate([FromBody] LoginRequest request)
        {
            try
            {
                UserToken userToken = await _authService.LoginAsync(request.Email, request.Password);

                User user = userToken.user;
                Token token = userToken.token;

                AuthResponse response = CreateAuthResponse(user, token);

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LocalSignup([FromBody] SignUpRequest request)
        {
            try
            {
                var challenge = await _authService.SignUpAsync(
                    request.Email,
                    request.Password,
                    request.Usertype
                );

                return StatusCode(
                    200,
                    new ApiResponse<VerificationChallengeResponse>(
                        "Verification email sent.",
                        new VerificationChallengeResponse
                        {
                            Challenge = challenge.Challenge,
                            ExpiresAtUtc = challenge.ExpiresAtUtc,
                        }
                    )
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

        [HttpPost("verify/otp")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LocalVerifyOtp([FromBody] OtpVerificationRequest request)
        {
            try
            {
                UserToken userToken = await _authService.VerifyOtpAsync(
                    request.Code,
                    request.Challenge
                );
                User user = userToken.user;
                Token authToken = userToken.token;

                AuthResponse response = CreateAuthResponse(user, authToken);

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        "Verification successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] LocalVerifyOtp failed: {e}");
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

                AuthResponse response = CreateAuthResponse(user, authToken);

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GoogleAuthenticate([FromBody] GoogleRequest request)
        {
            try
            {
                UserToken userToken = await _authService.GoogleAsync(request.Token);

                User user = userToken.user;
                Token token = userToken.token;

                AuthResponse response = CreateAuthResponse(user, token);

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MicrosoftAuthenticate([FromBody] MicrosoftRequest request)
        {
            try
            {
                UserToken userToken = await _authService.MicrosoftAsync(request.Token);

                User user = userToken.user;
                Token token = userToken.token;

                AuthResponse response = CreateAuthResponse(user, token);

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
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request)
        {
            try
            {
                string? refreshToken = HttpUtility.ResolveRefreshToken(Request, request?.RefreshToken);
                if (string.IsNullOrEmpty(refreshToken))
                    throw new UnauthorizedException("Missing refresh token");

                UserToken userToken = await _authService.HandleTokensAsync(refreshToken);

                User user = userToken.user;
                Token token = userToken.token;

                return Ok(CreateAuthResponse(user, token));
            }
            catch (Exception e)
            {
                HttpUtility.ClearRefreshToken(Response, _requestInfo);

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
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
        {
            try
            {
                string? refreshToken = HttpUtility.ResolveRefreshToken(Request, request?.RefreshToken);
                if (string.IsNullOrEmpty(refreshToken))
                {
                    HttpUtility.ClearRefreshToken(Response, _requestInfo);
                    return StatusCode(
                        200,
                        new MessageResponse($"The user is already logged out.")
                    );
                }

                await _authService.HandleLogoutAsync(refreshToken);
                HttpUtility.ClearRefreshToken(Response, _requestInfo);

                return StatusCode(
                    200,
                    new MessageResponse($"The user's logout is successful")
                );
            }
            catch (Exception e)
            {
                HttpUtility.ClearRefreshToken(Response, _requestInfo);

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

                AuthResponse response = CreateAuthResponse(user, authToken);

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var challenge = await _authService.ForgotPasswordAsync(request.Email);

                return StatusCode(
                    200,
                    new ApiResponse<VerificationChallengeResponse?>(
                        "If the account exist, we send a reset email",
                        challenge == null
                            ? null
                            : new VerificationChallengeResponse
                            {
                                Challenge = challenge.Challenge,
                                ExpiresAtUtc = challenge.ExpiresAtUtc,
                            }
                    )
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, [FromQuery] string token)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await _authService.ChangePasswordAsync(token, request.Password);
                }
                else if (!string.IsNullOrWhiteSpace(request.Code)
                    && !string.IsNullOrWhiteSpace(request.Challenge))
                {
                    await _authService.ChangePasswordWithOtpAsync(
                        request.Code,
                        request.Challenge,
                        request.Password
                    );
                }
                else
                {
                    throw new BadRequestException("Missing password reset token or OTP challenge.");
                }

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

        private AuthResponse CreateAuthResponse(User user, Token token)
        {
            var refreshToken = HttpUtility.ApplyRefreshToken(Response, _requestInfo, token.RefreshToken);
            return new AuthResponse(user.Id, user.Email, user.Usertype, token.AccessToken, refreshToken);
        }
    }
}
