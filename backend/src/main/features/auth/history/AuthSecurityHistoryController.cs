using System.ComponentModel.DataAnnotations;

using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.auth.history
{
    /// <summary>
    /// The signed-in account's own security history. There is no way to name another account:
    /// the owner is always the caller's token subject.
    /// </summary>
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/security-events")]
    public sealed class AuthSecurityHistoryController : ControllerBase
    {
        public const int MaxPage = 500;
        public const int MaxPageSize = 50;

        private readonly ISecurityHistoryService _securityHistoryService;

        public AuthSecurityHistoryController(ISecurityHistoryService securityHistoryService)
        {
            _securityHistoryService = securityHistoryService;
        }

        /// <summary>
        /// Lists the caller's security history from the last 90 days, newest first.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResponse<AccountSecurityEventResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(
            [FromQuery][Range(1, MaxPage)] int page = 1,
            [FromQuery][Range(1, MaxPageSize)] int pageSize = 20,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var user = User.GetUserPayload();
                var history = await _securityHistoryService.GetPageAsync(user.Id, page, pageSize, cancellationToken);

                return Ok(new ApiResponse<PagedResponse<AccountSecurityEventResponse>>(
                    "Security history fetched.",
                    history));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The client went away; there is nobody left to answer.
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthSecurityHistoryController] List failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}
