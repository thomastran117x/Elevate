using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.features.auth.oauth.links
{
    /// <summary>
    /// The signed-in owner's provider links: list them, link one explicitly, confirm a link a
    /// provider sign-in proposed by email, and unlink. Every change needs a fresh step-up.
    /// </summary>
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.Auth)]
    [Route("auth/providers")]
    public sealed class AuthProviderLinksController : ControllerBase
    {
        private readonly IProviderLinkService _providerLinks;

        public AuthProviderLinksController(IProviderLinkService providerLinks)
        {
            _providerLinks = providerLinks;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<ProviderLinksResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            try
            {
                var overview = await _providerLinks.GetOverviewAsync(User.GetUserPayload().Id, cancellationToken);

                return Ok(new ApiResponse<ProviderLinksResponse>(
                    "Sign-in providers fetched.",
                    new ProviderLinksResponse
                    {
                        HasPassword = overview.HasPassword,
                        Providers = overview.Providers
                            .Select(status => new ProviderLinkStatusResponse
                            {
                                Provider = status.Provider,
                                Available = status.Available,
                                Link = status.Link == null ? null : ToResponse(status.Link),
                                CanUnlink = status.CanUnlink,
                            })
                            .ToList(),
                    }));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthProviderLinksController] List failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        /// <summary>
        /// Starts a link: issues a state, nonce, and PKCE challenge bound to this session, and
        /// returns the provider URL to send the browser to.
        /// </summary>
        [HttpPost("{provider}/link/start")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [ProducesResponseType(typeof(ApiResponse<ProviderLinkStartResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        [AuthOperation(AuthOperation.FactorChange)]
        public async Task<IActionResult> Start(string provider, CancellationToken cancellationToken)
        {
            try
            {
                var start = await _providerLinks.StartAsync(CurrentSession(), provider, cancellationToken);

                return Ok(new ApiResponse<ProviderLinkStartResponse>(
                    "Continue with the provider to link it.",
                    new ProviderLinkStartResponse
                    {
                        AuthorizationUrl = start.AuthorizationUrl,
                        State = start.State,
                        RedirectUri = start.RedirectUri,
                        CodeVerifier = start.CodeVerifier,
                    }));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthProviderLinksController] Start failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpPost("{provider}/link/complete")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<ProviderLinkResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status410Gone)]
        [AuthOperation(AuthOperation.FactorChange)]
        public async Task<IActionResult> Complete(
            string provider,
            [FromBody] CompleteProviderLinkRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var link = await _providerLinks.CompleteAsync(
                    CurrentSession(),
                    provider,
                    request.State,
                    request.Code,
                    request.IdToken,
                    cancellationToken);

                return Ok(new ApiResponse<ProviderLinkResponse>("Provider linked.", ToResponse(link)));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthProviderLinksController] Complete failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        /// <summary>
        /// Confirms the link a provider sign-in proposed when its verified email matched this
        /// account. The caller must be signed in to that account and have stepped up.
        /// </summary>
        [HttpPost("link/confirm")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<ProviderLinkResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status410Gone)]
        [AuthOperation(AuthOperation.FactorChange)]
        public async Task<IActionResult> Confirm(
            [FromBody] ConfirmProviderLinkRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var link = await _providerLinks.ConfirmEmailLinkAsync(
                    User.GetUserPayload().Id,
                    request.LinkToken,
                    cancellationToken);

                return Ok(new ApiResponse<ProviderLinkResponse>("Provider linked.", ToResponse(link)));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthProviderLinksController] Confirm failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        [HttpDelete("{provider}")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        [AuthOperation(AuthOperation.FactorChange)]
        public async Task<IActionResult> Unlink(string provider, CancellationToken cancellationToken)
        {
            try
            {
                await _providerLinks.UnlinkAsync(User.GetUserPayload().Id, provider, cancellationToken);
                return Ok(new MessageResponse("Provider unlinked. Every session was signed out."));
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthProviderLinksController] Unlink failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }

        /// <summary>
        /// The session the access token names. <see cref="RequireMfaAttribute"/> has already
        /// required both claims, so their absence here is a malformed token.
        /// </summary>
        private ProviderLinkSession CurrentSession()
        {
            var sessionId = User.FindFirst(TokenService.SessionIdClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(sessionId)
                || !int.TryParse(User.FindFirst(TokenService.AuthVersionClaimType)?.Value, out var authVersion))
            {
                throw new UnauthorizedException("The session could not be identified.");
            }

            return new ProviderLinkSession(User.GetUserPayload().Id, sessionId, authVersion);
        }

        private static ProviderLinkResponse ToResponse(UserIdentityLink link) => new()
        {
            Provider = link.Provider,
            Email = link.Email,
            DisplayName = link.DisplayName,
            LinkedAtUtc = link.LinkedAtUtc,
            LastUsedAtUtc = link.LastUsedAtUtc,
        };
    }
}
