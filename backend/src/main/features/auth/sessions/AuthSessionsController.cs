using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.features.auth.sessions;

/// <summary>
/// The signed-in owner's sessions: list them, and sign out one, all others, or all of them.
/// Every revocation needs a fresh step-up and takes effect for both refresh and access tokens.
/// </summary>
[ApiController]
[Authorize]
[FeatureGate(FeatureFlagKeys.Auth)]
[Route("auth/sessions")]
public sealed class AuthSessionsController : ControllerBase
{
    private readonly ISessionManagementService _sessions;

    public AuthSessionsController(ISessionManagementService sessions)
    {
        _sessions = sessions;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AuthSessionsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List()
    {
        try
        {
            var sessions = await _sessions.ListAsync(User.GetUserPayload().Id, CurrentSessionId());
            return Ok(new ApiResponse<AuthSessionsResponse>("Sessions fetched.", sessions));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            Logger.Error($"[AuthSessionsController] List failed: {ex}");
            return HandleError.Resolve(ex);
        }
    }

    /// <summary>
    /// Matched before <see cref="Revoke"/>: a literal segment outranks a route parameter.
    /// </summary>
    [HttpDelete("others")]
    [RequireMfa]
    [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeOthers()
    {
        try
        {
            var result = await _sessions.RevokeOthersAsync(User.GetUserPayload().Id, CurrentSessionId());
            return Ok(new ApiResponse<SessionRevocationResponse>("Other sessions signed out.", result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            Logger.Error($"[AuthSessionsController] RevokeOthers failed: {ex}");
            return HandleError.Resolve(ex);
        }
    }

    [HttpDelete("{sessionId}")]
    [RequireMfa]
    [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(string sessionId)
    {
        try
        {
            var result = await _sessions.RevokeAsync(User.GetUserPayload().Id, CurrentSessionId(), sessionId);
            if (result.CurrentSessionRevoked)
                HttpUtility.ClearBrowserRefreshSession(Response);

            return Ok(new ApiResponse<SessionRevocationResponse>("Session signed out.", result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            Logger.Error($"[AuthSessionsController] Revoke failed: {ex}");
            return HandleError.Resolve(ex);
        }
    }

    [HttpDelete]
    [RequireMfa]
    [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
    [ProducesResponseType(typeof(ApiResponse<SessionRevocationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeAll()
    {
        try
        {
            var result = await _sessions.RevokeAllAsync(User.GetUserPayload().Id);
            HttpUtility.ClearBrowserRefreshSession(Response);

            return Ok(new ApiResponse<SessionRevocationResponse>("Every session signed out.", result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            Logger.Error($"[AuthSessionsController] RevokeAll failed: {ex}");
            return HandleError.Resolve(ex);
        }
    }

    /// <summary>
    /// The session the access token names. Token validation has already required the claim, so
    /// its absence here is a malformed token.
    /// </summary>
    private string CurrentSessionId()
    {
        var sessionId = User.FindFirst(TokenService.SessionIdClaimType)?.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new UnauthorizedException("The session could not be identified.");

        return sessionId;
    }
}
