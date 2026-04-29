using backend.main.dtos.requests.auth;
using backend.main.dtos.general;
using backend.main.dtos.responses.auth;
using backend.main.dtos.responses.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.configurations.application;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using backend.main.configurations.security;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route(RoutePaths.AuthPrefix)]
    public class AuthController : ControllerBase
    {
        private const string DefaultFrontendUrl = "http://localhost:3090";
        private readonly IAuthService _authService;
        private readonly IAntiforgery _antiforgery;
        private readonly ICaptchaService _captchaService;
        private readonly ClientRequestInfo _requestInfo;
        private readonly IConfiguration _configuration;

        public AuthController(
            IAuthService authService,
            IAntiforgery antiforgery,
            ICaptchaService captchaService,
            ClientRequestInfo requestInfo,
            IConfiguration configuration
        )
        {
            _authService = authService;
            _antiforgery = antiforgery;
            _captchaService = captchaService;
            _requestInfo = requestInfo;
            _configuration = configuration;
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> LocalAuthenticate([FromBody] LoginRequest request)
        {
            try
            {
                if (!await _captchaService.VerifyCaptchaAsync(request.Captcha))
                    throw new BadRequestException("Invalid captcha.");

                UserToken userToken = await _authService.LoginAsync(
                    request.Email,
                    request.Password,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.RememberMe
                );

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
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> LocalSignup([FromBody] SignUpRequest request)
        {
            try
            {
                if (!await _captchaService.VerifyCaptchaAsync(request.Captcha))
                    throw new BadRequestException("Invalid captcha.");

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
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> LocalVerifyOtp([FromBody] OtpVerificationRequest request)
        {
            try
            {
                UserToken userToken = await _authService.VerifyOtpAsync(
                    request.Code,
                    request.Challenge,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
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
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public IActionResult LocalVerify([FromQuery] string token)
        {
            var redirectUrl = BuildFrontendAuthUrl("verify", token);
            if (redirectUrl != null)
                return Redirect(redirectUrl);

            return Ok(
                new MessageResponse(
                    "Email verification requires confirmation from the frontend.",
                    "Open the verification link in the app and confirm to complete verification."
                )
            );
        }

        [HttpPost("verify")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> LocalVerify([FromBody] VerificationTokenRequest request)
        {
            try
            {
                UserToken userToken = await _authService.VerifyAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );
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

                Logger.Error($"[AuthController] LocalVerify POST failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("google")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> GoogleAuthenticate([FromBody] GoogleRequest request)
        {
            try
            {
                UserToken userToken = await _authService.GoogleAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.Nonce
                );

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
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> MicrosoftAuthenticate([FromBody] MicrosoftRequest request)
        {
            try
            {
                UserToken userToken = await _authService.MicrosoftAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );

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
                string? refreshToken = HttpUtility.ResolveBrowserRefreshToken(Request);
                string? sessionBindingToken = HttpUtility.ResolveBrowserSessionBindingToken(Request);
                if (string.IsNullOrEmpty(refreshToken))
                    throw new UnauthorizedException("Missing refresh token");

                UserToken userToken = await _authService.HandleTokensAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.BrowserCookie
                );

                User user = userToken.user;
                Token token = userToken.token;

                return Ok(CreateAuthResponse(user, token));
            }
            catch (Exception e)
            {
                HttpUtility.ClearBrowserRefreshSession(Response);

                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] Refresh failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("api/refresh")]
        public async Task<IActionResult> ApiRefresh([FromBody] RefreshTokenRequest? request)
        {
            try
            {
                string? refreshToken = HttpUtility.ResolveApiRefreshToken(Request, request?.RefreshToken);
                string? sessionBindingToken = HttpUtility.ResolveApiSessionBindingToken(
                    Request,
                    request?.SessionBindingToken
                );
                if (string.IsNullOrEmpty(refreshToken))
                    throw new UnauthorizedException("Missing refresh token");

                UserToken userToken = await _authService.HandleTokensAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.ApiToken
                );

                return Ok(CreateAuthResponse(userToken.user, userToken.token));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] ApiRefresh failed: {e}");
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
                string? refreshToken = HttpUtility.ResolveBrowserRefreshToken(Request);
                string? sessionBindingToken = HttpUtility.ResolveBrowserSessionBindingToken(Request);
                if (string.IsNullOrEmpty(refreshToken))
                {
                    HttpUtility.ClearBrowserRefreshSession(Response);
                    return StatusCode(
                        200,
                        new MessageResponse($"The user is already logged out.")
                    );
                }

                await _authService.HandleLogoutAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.BrowserCookie
                );
                HttpUtility.ClearBrowserRefreshSession(Response);

                return StatusCode(
                    200,
                    new MessageResponse($"The user's logout is successful")
                );
            }
            catch (Exception e)
            {
                HttpUtility.ClearBrowserRefreshSession(Response);

                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] Logout failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("api/logout")]
        public async Task<IActionResult> ApiLogout([FromBody] RefreshTokenRequest? request)
        {
            try
            {
                string? refreshToken = HttpUtility.ResolveApiRefreshToken(Request, request?.RefreshToken);
                string? sessionBindingToken = HttpUtility.ResolveApiSessionBindingToken(
                    Request,
                    request?.SessionBindingToken
                );
                if (string.IsNullOrEmpty(refreshToken))
                    throw new UnauthorizedException("Missing refresh token");
                if (string.IsNullOrEmpty(sessionBindingToken))
                    throw new UnauthorizedException("Missing session binding token");

                await _authService.HandleLogoutAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.ApiToken
                );

                return StatusCode(
                    200,
                    new MessageResponse($"The user's logout is successful")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] ApiLogout failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("device/verify")]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public IActionResult VerifyDevice([FromQuery] string token)
        {
            var redirectUrl = BuildFrontendAuthUrl("device/verify", token);
            if (redirectUrl != null)
                return Redirect(redirectUrl);

            return Ok(
                new MessageResponse(
                    "Device verification requires confirmation from the frontend.",
                    "Open the verification link in the app and confirm to complete device verification."
                )
            );
        }

        [HttpPost("device/verify")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> VerifyDevice([FromBody] VerificationTokenRequest request)
        {
            try
            {
                UserToken userToken = await _authService.VerifyDeviceLoginAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );
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

                Logger.Error($"[AuthController] VerifyDevice POST failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("forgot-password")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                var challenge = await _authService.ForgotPasswordAsync(request.Email);

                return StatusCode(
                    200,
                    new ApiResponse<VerificationChallengeResponse>(
                        "If the account exist, we send a reset email",
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

                Logger.Error($"[AuthController] ForgotPassword failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("change-password")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
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
            string? refreshToken = null;
            string? sessionBindingToken = null;

            if (token.Transport.UsesBrowserCookies())
            {
                HttpUtility.SetBrowserRefreshSession(
                    Response,
                    token.RefreshToken,
                    token.SessionBindingToken,
                    token.RefreshTokenLifetime
                );
            }
            else
            {
                refreshToken = token.RefreshToken;
                sessionBindingToken = token.SessionBindingToken;
            }

            return new AuthResponse(
                user.Id,
                user.Email,
                AuthRoles.NormalizeStored(user.Usertype),
                token.AccessToken,
                refreshToken,
                sessionBindingToken
            );
        }

        private string? BuildFrontendAuthUrl(string path, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var frontendBaseUrl = (
                _configuration["Frontend:BaseUrl"]
                ?? _configuration["FRONTEND_URL"]
                ?? DefaultFrontendUrl
            ).TrimEnd('/');

            return $"{frontendBaseUrl}/auth/{path}?token={Uri.EscapeDataString(token)}";
        }
    }
}
