using backend.main.application.environment;
using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.mfa.totp;
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
        private readonly IMfaEnrollmentService _smsService;

        public AuthTotpMfaController(
            ITotpMfaEnrollmentService totpService,
            IMfaEnrollmentService smsService
        )
        {
            _totpService = totpService;
            _smsService = smsService;
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
        [ProducesResponseType(typeof(ApiResponse<MfaStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyEnrollment([FromBody] TotpEnrollmentVerifyRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var totpEnrollment = await _totpService.VerifyEnrollmentAsync(user.Id, request.Code);
                var smsStatus = await _smsService.GetStatusAsync(user.Id);
                var combined = BuildCombinedStatus(smsStatus, totpEnrollment);

                return Ok(new ApiResponse<MfaStatusResponse>("TOTP MFA has been enabled.", combined));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] VerifyEnrollment failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("disable")]
        [ProducesResponseType(typeof(ApiResponse<MfaStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Disable([FromBody] TotpDisableRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var totpEnrollment = await _totpService.DisableAsync(user.Id, request.Code);
                var smsStatus = await _smsService.GetStatusAsync(user.Id);
                var combined = BuildCombinedStatus(smsStatus, totpEnrollment);

                return Ok(new ApiResponse<MfaStatusResponse>("TOTP MFA has been disabled.", combined));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTotpMfaController] Disable failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        private static MfaStatusResponse BuildCombinedStatus(
            MfaStatusResponse smsStatus,
            TotpMfaEnrollment? totpEnrollment
        )
        {
            return new MfaStatusResponse
            {
                SmsEnrollmentAvailable = smsStatus.SmsEnrollmentAvailable,
                IsSmsMfaEnabled = smsStatus.IsSmsMfaEnabled,
                MaskedPhoneNumber = smsStatus.MaskedPhoneNumber,
                PhoneVerifiedAtUtc = smsStatus.PhoneVerifiedAtUtc,
                TotpEnrollmentAvailable = EnvironmentSetting.AuthTotpMfaEnrollmentEnabled,
                IsTotpMfaEnabled = totpEnrollment?.IsTotpMfaEnabled ?? false,
                TotpEnrolledAtUtc = totpEnrollment?.EnrolledAtUtc,
            };
        }
    }
}
