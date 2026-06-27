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

namespace backend.main.features.auth.mfa
{
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/mfa")]
    public sealed class AuthMfaController : ControllerBase
    {
        private readonly IMfaEnrollmentService _mfaEnrollmentService;
        private readonly ITotpMfaEnrollmentService _totpMfaEnrollmentService;

        public AuthMfaController(
            IMfaEnrollmentService mfaEnrollmentService,
            ITotpMfaEnrollmentService totpMfaEnrollmentService
        )
        {
            _mfaEnrollmentService = mfaEnrollmentService;
            _totpMfaEnrollmentService = totpMfaEnrollmentService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<MfaStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var user = User.GetUserPayload();
                var smsStatus = await _mfaEnrollmentService.GetStatusAsync(user.Id);
                var totpEnrollment = await _totpMfaEnrollmentService.GetEnrollmentAsync(user.Id);

                var combined = new MfaStatusResponse
                {
                    SmsEnrollmentAvailable = smsStatus.SmsEnrollmentAvailable,
                    IsSmsMfaEnabled = smsStatus.IsSmsMfaEnabled,
                    MaskedPhoneNumber = smsStatus.MaskedPhoneNumber,
                    PhoneVerifiedAtUtc = smsStatus.PhoneVerifiedAtUtc,
                    TotpEnrollmentAvailable = EnvironmentSetting.AuthTotpMfaEnrollmentEnabled,
                    IsTotpMfaEnabled = totpEnrollment?.IsTotpMfaEnabled ?? false,
                    TotpEnrolledAtUtc = totpEnrollment?.EnrolledAtUtc,
                };

                return Ok(new ApiResponse<MfaStatusResponse>("MFA status fetched successfully.", combined));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaController] GetStatus failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("enroll/start")]
        [ProducesResponseType(typeof(ApiResponse<MfaChallengeResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> StartEnrollment([FromBody] MfaEnrollmentStartRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var challenge = await _mfaEnrollmentService.StartEnrollmentAsync(
                    user.Id,
                    request.PhoneNumber
                );

                return Ok(
                    new ApiResponse<MfaChallengeResponse>(
                        "SMS MFA enrollment code sent.",
                        challenge
                    )
                );
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaController] StartEnrollment failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("enroll/verify")]
        [ProducesResponseType(typeof(ApiResponse<MfaStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyEnrollment([FromBody] MfaEnrollmentVerifyRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var status = await _mfaEnrollmentService.VerifyEnrollmentAsync(
                    user.Id,
                    request.Code,
                    request.Challenge
                );

                return Ok(
                    new ApiResponse<MfaStatusResponse>(
                        "SMS MFA has been enabled.",
                        status
                    )
                );
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaController] VerifyEnrollment failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("disable")]
        [ProducesResponseType(typeof(ApiResponse<MfaStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Disable([FromBody] MfaDisableRequest _)
        {
            try
            {
                var user = User.GetUserPayload();
                var status = await _mfaEnrollmentService.DisableAsync(user.Id);

                return Ok(
                    new ApiResponse<MfaStatusResponse>(
                        "SMS MFA has been disabled.",
                        status
                    )
                );
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaController] Disable failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}
