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
        private readonly IMfaSettingsBuilder _settingsBuilder;

        public AuthMfaController(
            IMfaEnrollmentService mfaEnrollmentService,
            IMfaSettingsBuilder settingsBuilder
        )
        {
            _mfaEnrollmentService = mfaEnrollmentService;
            _settingsBuilder = settingsBuilder;
        }

        [HttpGet]
        [RequireMfa]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var user = User.GetUserPayload();
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);
                return Ok(new ApiResponse<MfaSettingsResponse>("MFA status fetched successfully.", settings));
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
        [HttpPost("sms/enroll/start")]
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

        [HttpPost("enable/start")]
        [HttpPost("sms/enable/start")]
        [ProducesResponseType(typeof(ApiResponse<MfaChallengeResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> StartEnable()
        {
            try
            {
                var user = User.GetUserPayload();
                var challenge = await _mfaEnrollmentService.StartEnableAsync(user.Id);

                return Ok(new ApiResponse<MfaChallengeResponse>("SMS MFA re-enable code sent.", challenge));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaController] StartEnable failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("enroll/verify")]
        [HttpPost("sms/enroll/verify")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> VerifyEnrollment([FromBody] MfaEnrollmentVerifyRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                await _mfaEnrollmentService.VerifyEnrollmentAsync(
                    user.Id,
                    request.Code,
                    request.Challenge
                );
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(
                    new ApiResponse<MfaSettingsResponse>(
                        "SMS MFA has been enabled.",
                        settings
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
        [HttpPost("sms/disable")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Disable([FromBody] MfaDisableRequest _)
        {
            try
            {
                var user = User.GetUserPayload();
                await _mfaEnrollmentService.DisableAsync(user.Id);
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(
                    new ApiResponse<MfaSettingsResponse>(
                        "SMS MFA has been disabled.",
                        settings
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

        [HttpPost("remove")]
        [HttpPost("sms/remove")]
        [ProducesResponseType(typeof(ApiResponse<MfaSettingsResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Remove([FromBody] MfaDisableRequest _)
        {
            try
            {
                var user = User.GetUserPayload();
                await _mfaEnrollmentService.RemoveAsync(user.Id);
                var settings = await _settingsBuilder.BuildAsync(user.Id, user.Email);

                return Ok(
                    new ApiResponse<MfaSettingsResponse>(
                        "SMS MFA has been removed.",
                        settings
                    )
                );
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthMfaController] Remove failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}
