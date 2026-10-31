using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.features.auth.mfa.recovery
{
    /// <summary>
    /// Single-use recovery codes: a status read, and generation, which replaces any earlier set
    /// and so needs a fresh step-up proof.
    /// </summary>
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/mfa/recovery-codes")]
    public sealed class AuthRecoveryCodesController : ControllerBase
    {
        private readonly IRecoveryCodeService _recoveryCodeService;
        private readonly IMfaFactorChangeService _factorChangeService;

        public AuthRecoveryCodesController(
            IRecoveryCodeService recoveryCodeService,
            IMfaFactorChangeService factorChangeService)
        {
            _recoveryCodeService = recoveryCodeService;
            _factorChangeService = factorChangeService;
        }

        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResponse<RecoveryCodeStatusResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var user = User.GetUserPayload();
                var status = await _recoveryCodeService.GetStatusAsync(user.Id);

                return Ok(new ApiResponse<RecoveryCodeStatusResponse>(
                    "Recovery code status fetched.",
                    new RecoveryCodeStatusResponse
                    {
                        RemainingCount = status.RemainingCount,
                        TotalCount = RecoveryCodeFormat.SetSize,
                        GeneratedAtUtc = status.GeneratedAtUtc,
                    }));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthRecoveryCodesController] GetStatus failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        /// <summary>
        /// Generates a new set of ten codes, invalidating every earlier code. The response is the
        /// only time the codes are shown, so it must not be cached.
        /// </summary>
        [HttpPost("regenerate")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(ApiResponse<RecoveryCodesResponse>), StatusCodes.Status200OK)]
        [AuthOperation(AuthOperation.FactorChange)]
        public async Task<IActionResult> Regenerate()
        {
            try
            {
                var user = User.GetUserPayload();
                var set = await _recoveryCodeService.RegenerateAsync(user.Id);
                await _factorChangeService.RecordAsync(
                    user.Id,
                    user.Email,
                    MfaFactorKind.RecoveryCodes,
                    set.Replaced ? MfaFactorChange.Regenerated : MfaFactorChange.Enabled);

                return Ok(new ApiResponse<RecoveryCodesResponse>(
                    "Recovery codes generated. Save them now; they will not be shown again.",
                    new RecoveryCodesResponse
                    {
                        Codes = set.Codes,
                        GeneratedAtUtc = set.GeneratedAtUtc,
                    }));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthRecoveryCodesController] Regenerate failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}
