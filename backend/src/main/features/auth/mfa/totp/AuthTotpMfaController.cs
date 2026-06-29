using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.auth.mfa.totp
{
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/mfa/totp")]
    public sealed class AuthTotpMfaController : ControllerBase
    {
        private readonly ITotpMfaEnrollmentService _totpService;
        private readonly IMfaSettingsBuilder _settingsBuilder;

        public AuthTotpMfaController(
            ITotpMfaEnrollmentService totpService,
            IMfaSettingsBuilder settingsBuilder
        )
        {
            _totpService = totpService;
            _settingsBuilder = settingsBuilder;
        }

        [HttpPost("enroll/start")]
        [ProducesResponseType(typeof(ApiResponse<TotpEnrollmentStartResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> StartEnrollment()
        {
            try
            {
                var user = User.GetUserPayload();
                var response = await _totpService.StartEnrollmentAsync(user.Id, user.Email);

                return Ok(new ApiResponse<TotpEnrollmentStartResponse>(
                    "TOTP enrollment started. Scan the QR code with your authenticator app.",
                    response
                ));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] StartEnrollment failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("enroll/verify")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyEnrollment([FromBody] TotpEnrollmentVerifyRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await _totpService.VerifyEnrollmentAsync(user.Id, request.Code);
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(new ApiResponse<MfaSettingsResponse>("TOTP MFA has been enabled.", settings));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] VerifyEnrollment failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("enable")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Enable([FromBody] TotpDisableRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await _totpService.EnableAsync(user.Id, request.Code);
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(new ApiResponse<MfaSettingsResponse>("TOTP MFA has been enabled.", settings));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] Enable failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("disable")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Disable([FromBody] TotpDisableRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await _totpService.DisableAsync(user.Id, request.Code);
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(new ApiResponse<MfaSettingsResponse>("TOTP MFA has been disabled.", settings));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] Disable failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("remove")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Remove([FromBody] TotpDisableRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await _totpService.RemoveAsync(user.Id, request.Code);
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(new ApiResponse<MfaSettingsResponse>("TOTP MFA has been removed.", settings));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] Remove failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}


