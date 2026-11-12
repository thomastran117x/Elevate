using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.auth.abuse;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.main.features.auth.passkeys
{
    /// <summary>
    /// Passkeys (WebAuthn credentials): register and manage them on the signed-in account, sign in
    /// with one, and use one for a fresh step-up. Each ceremony is two calls: options, then the
    /// authenticator's response with the ceremony id the options returned.
    /// </summary>
    /// <remarks>
    /// CSRF is enforced for every state-changing request here by <c>UseRefreshCsrfValidation</c>,
    /// which runs before authentication. The anonymous sign-in actions deliberately carry no
    /// <c>[ValidateAntiForgeryToken]</c>: that filter runs after authentication, so a page that
    /// fetched its token anonymously and then restored a session (sending a bearer token) would
    /// be refused, as the login page's passkey autofill does on every visit by a signed-in user.
    /// </remarks>
    [ApiController]
    [Authorize]
    [FeatureGate(FeatureFlagKeys.AuthPasskeys)]
    [Route("auth/passkeys")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AuthPasskeysController : ControllerBase
    {
        private readonly IPasskeyManagementService _management;
        private readonly IPasskeyAuthenticationService _authentication;
        private readonly IAuthAbuseProtectionService _abuseProtection;
        private readonly ClientRequestInfo _requestInfo;

        public AuthPasskeysController(
            IPasskeyManagementService management,
            IPasskeyAuthenticationService authentication,
            IAuthAbuseProtectionService abuseProtection,
            ClientRequestInfo requestInfo)
        {
            _management = management;
            _authentication = authentication;
            _abuseProtection = abuseProtection;
            _requestInfo = requestInfo;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PasskeysResponse>), StatusCodes.Status200OK)]
        public Task<IActionResult> List() => RunAsync(nameof(List), async () =>
        {
            var passkeys = await _management.ListAsync(User.GetUserPayload().Id, HttpContext.RequestAborted);
            return Ok(new ApiResponse<PasskeysResponse>("Passkeys fetched.", passkeys));
        });

        [HttpPost("registration/options")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<PasskeyCeremonyOptionsResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        public Task<IActionResult> StartRegistration([FromBody] PasskeyRegistrationOptionsRequest request) =>
            RunAsync(nameof(StartRegistration), async () =>
            {
                var options = await _management.StartRegistrationAsync(
                    ResolveAccount(),
                    request.Name,
                    HttpContext.RequestAborted);
                return Ok(new ApiResponse<PasskeyCeremonyOptionsResponse>("Passkey registration started.", options));
            });

        [HttpPost("registration")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
        [AuthOperation(AuthOperation.FactorChange)]
        [ProducesResponseType(typeof(ApiResponse<PasskeyResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        public Task<IActionResult> CompleteRegistration([FromBody] PasskeyCeremonyCompletionRequest request) =>
            RunAsync(nameof(CompleteRegistration), async () =>
            {
                var passkey = await _management.CompleteRegistrationAsync(
                    ResolveAccount(),
                    request.CeremonyId,
                    request.Credential,
                    HttpContext.RequestAborted);
                return Ok(new ApiResponse<PasskeyResponse>("Passkey added.", passkey));
            });

        [HttpPatch("{passkeyId:int}")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<PasskeyResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
        public Task<IActionResult> Rename(int passkeyId, [FromBody] RenamePasskeyRequest request) =>
            RunAsync(nameof(Rename), async () =>
            {
                var passkey = await _management.RenameAsync(
                    User.GetUserPayload().Id,
                    passkeyId,
                    request.Name,
                    HttpContext.RequestAborted);
                return Ok(new ApiResponse<PasskeyResponse>("Passkey renamed.", passkey));
            });

        [HttpDelete("{passkeyId:int}")]
        [RequireMfa]
        [EnableRateLimiting(RateLimiterConfiguration.SessionManagementPolicyName)]
        [AuthOperation(AuthOperation.FactorChange)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status409Conflict)]
        public Task<IActionResult> Remove(int passkeyId) => RunAsync(nameof(Remove), async () =>
        {
            await _management.RemoveAsync(ResolveAccount(), passkeyId, HttpContext.RequestAborted);
            return Ok(new ApiResponse<object?>("Passkey removed.", null));
        });

        [HttpPost("sign-in/options")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [ProducesResponseType(typeof(ApiResponse<PasskeyCeremonyOptionsResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
        public Task<IActionResult> StartSignIn() => RunAsync(nameof(StartSignIn), async () =>
        {
            await _abuseProtection.EnsureSourceAllowedAsync(
                AuthAbuseFlow.General,
                _requestInfo.IpAddress,
                HttpContext.RequestAborted);
            var options = await _authentication.StartSignInAsync();
            return Ok(new ApiResponse<PasskeyCeremonyOptionsResponse>("Passkey sign-in started.", options));
        });

        [HttpPost("sign-in")]
        [AllowAnonymous]
        [EnableRateLimiting(RateLimiterConfiguration.AuthPolicyName)]
        [AuthOperation(AuthOperation.SignIn)]
        [ProducesResponseType(typeof(ApiResponse<LoginAuthenticationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
        public Task<IActionResult> CompleteSignIn([FromBody] PasskeySignInRequest request) =>
            RunAsync(nameof(CompleteSignIn), async () =>
            {
                await _abuseProtection.EnsureSourceAllowedAsync(
                    AuthAbuseFlow.General,
                    _requestInfo.IpAddress,
                    HttpContext.RequestAborted);
                var userToken = await _authentication.CompleteSignInAsync(
                    request.CeremonyId,
                    request.Credential,
                    SessionTransportResolver.ResolveOrDefault(request.Transport),
                    request.RememberMe,
                    HttpContext.RequestAborted);

                // The same shape as password sign-in, so a client handles both alike.
                var response = new LoginAuthenticationResponse
                {
                    Type = AuthFlowResponseTypes.Authenticated,
                    Auth = AuthSessionResponses.Create(Response, userToken.token),
                };
                return Ok(new ApiResponse<LoginAuthenticationResponse>("Login successful", response));
            });

        /// <summary>Not itself gated by <c>[RequireMfa]</c>: it is how the gate is satisfied.</summary>
        [HttpPost("step-up/options")]
        [ProducesResponseType(typeof(ApiResponse<PasskeyCeremonyOptionsResponse>), StatusCodes.Status200OK)]
        public Task<IActionResult> StartStepUp() => RunAsync(nameof(StartStepUp), async () =>
        {
            var account = ResolveAccount();
            await ProtectStepUpAsync(account.UserId);
            var options = await _authentication.StartStepUpAsync(account, HttpContext.RequestAborted);
            return Ok(new ApiResponse<PasskeyCeremonyOptionsResponse>("Passkey verification started.", options));
        });

        /// <summary>Not itself gated by <c>[RequireMfa]</c>: it is how the gate is satisfied.</summary>
        [HttpPost("step-up")]
        [AuthOperation(AuthOperation.StepUpVerify)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
        public Task<IActionResult> CompleteStepUp([FromBody] PasskeyCeremonyCompletionRequest request) =>
            RunAsync(nameof(CompleteStepUp), async () =>
            {
                var account = ResolveAccount();
                await ProtectStepUpAsync(account.UserId);
                await _authentication.CompleteStepUpAsync(
                    account,
                    request.CeremonyId,
                    request.Credential,
                    HttpContext.RequestAborted);
                return Ok(new ApiResponse<object?>("Multi-factor verification successful.", null));
            });

        private PasskeyAccount ResolveAccount()
        {
            var user = User.GetUserPayload();
            var sessionId = User.FindFirst(TokenService.SessionIdClaimType)?.Value;
            if (string.IsNullOrWhiteSpace(sessionId)
                || !int.TryParse(User.FindFirst(TokenService.AuthVersionClaimType)?.Value, out var authVersion))
            {
                throw new UnauthorizedException("This session cannot be verified. Please sign in again.");
            }

            return new PasskeyAccount(user.Id, user.Email, sessionId, authVersion);
        }

        private async Task ProtectStepUpAsync(int userId)
        {
            await _abuseProtection.EnsureSourceAllowedAsync(
                AuthAbuseFlow.Verification,
                _requestInfo.IpAddress,
                HttpContext.RequestAborted);
            await _abuseProtection.EnsureTargetAllowedAsync(
                AuthAbuseFlow.Verification,
                AuthAbuseTargetKind.AccountId,
                userId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                HttpContext.RequestAborted);
        }

        private static async Task<IActionResult> RunAsync(string action, Func<Task<IActionResult>> handler)
        {
            try
            {
                return await handler();
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    return HandleError.Resolve(ex);

                Logger.Error($"[AuthPasskeysController] {action} failed: {ex}");
                return HandleError.Resolve(ex);
            }
        }
    }
}
