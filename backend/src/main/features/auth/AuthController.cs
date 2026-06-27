using backend.main.application.bootstrap;
using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth;
using backend.main.features.auth.captcha;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.features.auth
{
    /// <summary>
    /// Authentication, session, verification, and password-recovery endpoints.
    /// </summary>
    [ApiController]
    [FeatureGate(FeatureFlagKeys.Auth)]
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
        [ProducesResponseType(typeof(ApiResponse<LoginAuthenticationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LocalAuthenticate([FromBody] LoginRequest request)
        {
            try
            {
                if (!await _captchaService.VerifyCaptchaAsync(request.Captcha))
                    throw new BadRequestException("Invalid captcha.");

                var result = await _authService.LoginAsync(
                    request.Email,
                    request.Password,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.RememberMe,
                    request.ReturnUrl
                );

                var response = CreateLoginAuthenticationResponse(result);
                return StatusCode(
                    200,
                    new ApiResponse<LoginAuthenticationResponse>(ResolveLoginMessage(response.Type), response)
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
        [ProducesResponseType(typeof(ApiResponse<VerificationChallengeResponse>), StatusCodes.Status200OK)]
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
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LocalVerifyOtp([FromBody] OtpVerificationRequest request)
        {
            try
            {
                var userToken = await _authService.VerifyOtpAsync(
                    request.Code,
                    request.Challenge,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Verification successful",
                        CreateSessionResponse(userToken.user, userToken.token)
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
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status302Found)]
        public IActionResult LocalVerify([FromQuery] string token)
        {
            var redirectUrl = BuildFrontendAuthUrl("verify", token);
            if (redirectUrl != null)
                return Redirect(redirectUrl);

            return Ok(
                new MessageResponse(
                    "Email verification requires confirmation from the frontend. Open the verification link in the app and confirm to complete verification."
                )
            );
        }

        [HttpPost("verify")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> LocalVerify([FromBody] VerificationTokenRequest request)
        {
            try
            {
                var userToken = await _authService.VerifyAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Verification successful",
                        CreateSessionResponse(userToken.user, userToken.token)
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
        [ProducesResponseType(typeof(ApiResponse<OAuthAuthenticationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GoogleAuthenticate([FromBody] GoogleRequest request)
        {
            try
            {
                var result = await _authService.GoogleAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.Nonce,
                    request.ReturnUrl
                );
                var response = CreateOAuthAuthenticationResponse(result);

                return StatusCode(
                    200,
                    new ApiResponse<OAuthAuthenticationResponse>(ResolveOAuthMessage(response.Type), response)
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

        [HttpPost("google/code")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<OAuthAuthenticationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GoogleCodeAuthenticate([FromBody] GoogleCodeRequest request)
        {
            try
            {
                var result = await _authService.GoogleCodeAsync(
                    request.Code,
                    request.CodeVerifier,
                    request.RedirectUri,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.Nonce,
                    request.ReturnUrl
                );
                var response = CreateOAuthAuthenticationResponse(result);

                return StatusCode(
                    200,
                    new ApiResponse<OAuthAuthenticationResponse>(ResolveOAuthMessage(response.Type), response)
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] GoogleCodeAuthenticate failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("microsoft")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<OAuthAuthenticationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MicrosoftAuthenticate([FromBody] MicrosoftRequest request)
        {
            try
            {
                var result = await _authService.MicrosoftAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.Nonce,
                    request.ReturnUrl
                );
                var response = CreateOAuthAuthenticationResponse(result);

                return StatusCode(
                    200,
                    new ApiResponse<OAuthAuthenticationResponse>(ResolveOAuthMessage(response.Type), response)
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

        [Authorize]
        [HttpGet("me")]
        [ProducesResponseType(typeof(ApiResponse<CurrentUserResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Me()
        {
            try
            {
                var userPayload = User.GetUserPayload();
                var user = await _authService.GetCurrentUserAsync(userPayload.Id);

                return StatusCode(
                    200,
                    new ApiResponse<CurrentUserResponse>(
                        "Current user fetched successfully.",
                        CreateCurrentUserResponse(user)
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] Me failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("refresh")]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest? request)
        {
            try
            {
                string? refreshToken = HttpUtility.ResolveBrowserRefreshToken(Request);
                string? sessionBindingToken = HttpUtility.ResolveBrowserSessionBindingToken(Request);
                if (string.IsNullOrEmpty(refreshToken))
                    throw new UnauthorizedException("Missing refresh token");

                var userToken = await _authService.HandleTokensAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.BrowserCookie
                );

                return Ok(
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Session refreshed successfully.",
                        CreateSessionResponse(userToken.user, userToken.token)
                    )
                );
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

        [HttpPost("oauth/complete")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CompleteOAuthSignup([FromBody] CompleteOAuthSignupRequest request)
        {
            try
            {
                var userToken = await _authService.CompleteOAuthSignupAsync(
                    request.SignupToken,
                    request.Usertype,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Signup completed successfully.",
                        CreateSessionResponse(userToken.user, userToken.token)
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] CompleteOAuthSignup failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("api/refresh")]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
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

                var userToken = await _authService.HandleTokensAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.ApiToken
                );

                return Ok(
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Session refreshed successfully.",
                        CreateSessionResponse(userToken.user, userToken.token)
                    )
                );
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
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult Csrf()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            return Ok(
                new ApiResponse<object>(
                    "CSRF token fetched successfully.",
                    new
                    {
                        token = tokens.RequestToken
                    }
                )
            );
        }

        [HttpPost("logout")]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
        {
            try
            {
                string? refreshToken = HttpUtility.ResolveBrowserRefreshToken(Request);
                string? sessionBindingToken = HttpUtility.ResolveBrowserSessionBindingToken(Request);
                if (string.IsNullOrEmpty(refreshToken))
                {
                    HttpUtility.ClearBrowserRefreshSession(Response);
                    return StatusCode(200, new MessageResponse("The user is already logged out."));
                }

                await _authService.HandleLogoutAsync(
                    refreshToken,
                    sessionBindingToken,
                    SessionTransport.BrowserCookie
                );
                HttpUtility.ClearBrowserRefreshSession(Response);

                return StatusCode(200, new MessageResponse("The user's logout is successful"));
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
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
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

                return StatusCode(200, new MessageResponse("The user's logout is successful"));
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
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status302Found)]
        public IActionResult VerifyDevice([FromQuery] string token)
        {
            var redirectUrl = BuildFrontendAuthUrl("device/verify", token);
            if (redirectUrl != null)
                return Redirect(redirectUrl);

            return Ok(
                new MessageResponse(
                    "Device verification requires confirmation from the frontend. Open the verification link in the app and confirm to complete device verification."
                )
            );
        }

        [HttpPost("device/verify")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyDevice([FromBody] VerificationTokenRequest request)
        {
            try
            {
                var result = await _authService.VerifyDeviceLoginAsync(
                    request.Token,
                    SessionTransportResolver.ResolveOrDefault(request.Transport)
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Device verified. Login successful.",
                        CreateSessionResponse(result)
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

        [HttpPost("mfa/start")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<StartLoginStepUpResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> StartStepUp([FromBody] StartLoginStepUpRequest request)
        {
            try
            {
                var response = await _authService.StartLoginStepUpAsync(request.Challenge, request.Method);
                return Ok(new ApiResponse<StartLoginStepUpResponse>("Sign-in verification sent.", response));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] StartStepUp failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("mfa/verify")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyStepUp([FromBody] VerifyLoginStepUpRequest request)
        {
            try
            {
                var response = await _authService.VerifyLoginStepUpAsync(request.Challenge, request.Code);
                return Ok(
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Sign-in verification successful.",
                        CreateSessionResponse(response)
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] VerifyStepUp failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("mfa/verify/totp")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<AuthenticatedSessionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyTotpStepUp([FromBody] VerifyLoginStepUpRequest request)
        {
            try
            {
                var response = await _authService.VerifyTotpLoginStepUpAsync(request.Challenge, request.Code);
                return Ok(
                    new ApiResponse<AuthenticatedSessionResponse>(
                        "Sign-in verification successful.",
                        CreateSessionResponse(response)
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] VerifyTotpStepUp failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("forgot-password")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<VerificationChallengeResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                if (!await _captchaService.VerifyCaptchaAsync(request.Captcha))
                    throw new BadRequestException("Invalid captcha.");

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
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, [FromQuery] string? token)
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

                return StatusCode(200, new MessageResponse("Password reset successful. Please login"));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[AuthController] ChangePassword failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        private AuthenticatedSessionResponse CreateSessionResponse(
            User user,
            Token token,
            string? returnPath = null
        )
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

            return new AuthenticatedSessionResponse(
                token.AccessToken,
                token.AccessTokenExpiresAtUtc,
                refreshToken,
                sessionBindingToken,
                returnPath
            );
        }

        private AuthenticatedSessionResponse CreateSessionResponse(AuthenticatedSessionResult result)
        {
            return CreateSessionResponse(
                result.UserToken.user,
                result.UserToken.token,
                result.ReturnPath
            );
        }

        private LoginAuthenticationResponse CreateLoginAuthenticationResponse(LoginAuthenticationResult result)
        {
            return new LoginAuthenticationResponse
            {
                Type = result.Type,
                Auth = result.Session != null ? CreateSessionResponse(result.Session) : null,
                StepUp = result.StepUp
            };
        }

        private OAuthAuthenticationResponse CreateOAuthAuthenticationResponse(OAuthAuthenticationResult result)
        {
            return new OAuthAuthenticationResponse
            {
                Type = result.Type,
                Auth = result.Session != null ? CreateSessionResponse(result.Session) : null,
                StepUp = result.StepUp,
                RoleSelection = result.PendingSignup == null
                    ? null
                    : new OAuthRoleSelectionResponse
                    {
                        SignupToken = result.PendingSignup.SignupToken,
                        Email = result.PendingSignup.Email,
                        Name = result.PendingSignup.Name,
                        Provider = result.PendingSignup.Provider,
                    }
            };
        }

        private static string ResolveLoginMessage(string type) => type switch
        {
            AuthFlowResponseTypes.RequiresStepUp => "Additional sign-in verification is required.",
            _ => "Login successful"
        };

        private static string ResolveOAuthMessage(string type) => type switch
        {
            AuthFlowResponseTypes.RequiresRoleSelection => "Role selection is required to complete signup.",
            AuthFlowResponseTypes.RequiresStepUp => "Additional sign-in verification is required.",
            _ => "Login successful"
        };

        private static CurrentUserResponse CreateCurrentUserResponse(User user)
        {
            return new CurrentUserResponse
            {
                Id = user.Id,
                Email = user.Email,
                Username = string.IsNullOrWhiteSpace(user.Username) ? user.Email : user.Username,
                Name = user.Name,
                Avatar = user.Avatar,
                Usertype = AuthRoles.NormalizeStored(user.Usertype),
            };
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
