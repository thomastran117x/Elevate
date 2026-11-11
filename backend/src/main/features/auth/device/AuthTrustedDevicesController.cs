using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.features.auth.device
{
    /// <summary>
    /// The signed-in owner's trusted devices, which skip sign-in verification: list them, and
    /// forget one or all of them. Forgetting needs a fresh step-up.
    /// </summary>
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/devices")]
    public sealed class AuthTrustedDevicesController : ControllerBase
    {
        private readonly ITrustedDeviceManagementService _devices;
        private readonly ClientRequestInfo _requestInfo;

        public AuthTrustedDevicesController(ITrustedDeviceManagementService devices, ClientRequestInfo requestInfo)
        {
            _devices = devices;
            _requestInfo = requestInfo;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<TrustedDevicesResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List()
        {
            try
            {
                var devices = await _devices.ListAsync(
                    User.GetUserPayload().Id,
                    HttpUtility.ResolveTrustedDeviceToken(Request));
                return Ok(new ApiResponse<TrustedDevicesResponse>("Trusted devices fetched.", devices));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTrustedDevicesController] List failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpDelete("{deviceId:int}")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<TrustedDeviceRevocationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Revoke(int deviceId)
        {
            try
            {
                var result = await _devices.RevokeAsync(
                    User.GetUserPayload().Id,
                    deviceId,
                    HttpUtility.ResolveTrustedDeviceToken(Request));
                if (result.CurrentDeviceRevoked)
                    HttpUtility.ClearTrustedDeviceToken(Response, _requestInfo);

                return Ok(new ApiResponse<TrustedDeviceRevocationResponse>("Trusted device forgotten.", result));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTrustedDevicesController] Revoke failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpDelete]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<TrustedDeviceRevocationResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RevokeAll()
        {
            try
            {
                var result = await _devices.RevokeAllAsync(
                    User.GetUserPayload().Id,
                    HttpUtility.ResolveTrustedDeviceToken(Request));
                HttpUtility.ClearTrustedDeviceToken(Response, _requestInfo);

                return Ok(new ApiResponse<TrustedDeviceRevocationResponse>("Every trusted device forgotten.", result));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthTrustedDevicesController] RevokeAll failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}
