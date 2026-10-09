using System.Security.Claims;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.abuse;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.auth.mfa.session
{
    /// <summary>
    /// In-session MFA "step-up" endpoints used to satisfy <c>[RequireMfa]</c> routes.
    /// These must NOT themselves be gated by <c>[RequireMfa]</c>.
    /// </summary>
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/mfa/step-up")]
    public sealed class AuthMfaStepUpController : ControllerBase
    {
        private readonly ISessionMfaVerificationService _sessionMfaVerificationService;
        private readonly IAuthAbuseProtectionService _abuseProtection;
        private readonly ClientRequestInfo _requestInfo;

        public AuthMfaStepUpController(
            ISessionMfaVerificationService sessionMfaVerificationService,
            IAuthAbuseProtectionService abuseProtection,
            ClientRequestInfo requestInfo)
        {
            _sessionMfaVerificationService = sessionMfaVerificationService;
            _abuseProtection = abuseProtection;
            _requestInfo = requestInfo;
        }

        [HttpGet("options")]
        [ProducesResponseType(typeof(ApiResponse<SessionMfaOptionsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOptions()
        {
            try
            {
                var user = User.GetUserPayload();
                var options = await _sessionMfaVerificationService.GetOptionsAsync(user.Id, user.Email);
                return Ok(new ApiResponse<SessionMfaOptionsResponse>("MFA verification options fetched.", options));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaStepUpController] GetOptions failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        /// <summary>
        /// Gated probe used by the frontend to decide whether a screen behind
        /// <c>[RequireMfa]</c> can be shown. Returns 200 while the session holds a fresh
        /// step-up proof, otherwise the filter short-circuits with 403 MFA_REQUIRED.
        /// </summary>
        [HttpGet("status")]
        [RequireMfa]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
        public IActionResult Status()
        {
            return Ok(new ApiResponse<object?>("Session MFA verification is active.", null));
        }

        [HttpPost("start")]
        [ProducesResponseType(typeof(ApiResponse<SessionMfaStartResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Start([FromBody] SessionMfaStartRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await ProtectAsync(AuthAbuseFlow.MfaDelivery, user.Id, HttpContext.RequestAborted);
                var result = await _sessionMfaVerificationService.StartAsync(user.Id, user.Email, request.Method);
                return Ok(new ApiResponse<SessionMfaStartResponse>("MFA verification code sent.", result));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaStepUpController] Start failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("verify")]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Verify([FromBody] SessionMfaVerifyRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await ProtectAsync(AuthAbuseFlow.Verification, user.Id, HttpContext.RequestAborted);
                var sessionId = User.FindFirst(TokenService.SessionIdClaimType)?.Value;
                if (!int.TryParse(User.FindFirst(TokenService.AuthVersionClaimType)?.Value, out var authVersion))
                    throw new UnauthorizedException("This session cannot be verified. Please sign in again.");

                await _sessionMfaVerificationService.VerifyAsync(
                    user.Id,
                    user.Email,
                    sessionId ?? string.Empty,
                    authVersion,
                    request.Method,
                    request.Code
                );

                return Ok(new ApiResponse<object?>("Multi-factor verification successful.", null));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaStepUpController] Verify failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        private async Task ProtectAsync(
            AuthAbuseFlow flow,
            int accountId,
            CancellationToken cancellationToken)
        {
            await _abuseProtection.EnsureSourceAllowedAsync(
                flow,
                _requestInfo.IpAddress,
                cancellationToken);
            await _abuseProtection.EnsureTargetAllowedAsync(
                flow,
                AuthAbuseTargetKind.AccountId,
                accountId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken);
        }
    }
}
