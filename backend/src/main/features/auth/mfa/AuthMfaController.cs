using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
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

        public AuthMfaController(IMfaEnrollmentService mfaEnrollmentService)
        {
            _mfaEnrollmentService = mfaEnrollmentService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<MfaStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var user = User.GetUserPayload();
                var status = await _mfaEnrollmentService.GetStatusAsync(user.Id);

                return Ok(
                    new ApiResponse<MfaStatusResponse>(
                        "SMS MFA status fetched successfully.",
                        status
                    )
                );
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
