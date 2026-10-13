using System.Security.Cryptography;
using System.Text;

using backend.main.features.auth.notifications;
using backend.main.features.auth.stepup;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.profile;
using backend.main.shared.exceptions.app;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.features.auth.device
{
    public class DeviceService : IDeviceService
    {
        private readonly IDeviceRepository _deviceRepository;
        private readonly IAuthUserRepository _userRepository;
        private readonly ICacheService _cacheService;
        private readonly IAuthNotificationService _authNotificationService;
        private readonly ClientRequestInfo _requestInfo;
        private readonly IDeviceTrustService _deviceTrustService;
        private readonly IAuthSessionService _authSessionService;
        private readonly ILoginStepUpChallengeService _loginStepUpChallengeService;
        private readonly ITokenService _tokenService;
        private readonly TimeSpan _pendingTtl = TimeSpan.FromMinutes(15);

        public DeviceService(
            IDeviceRepository deviceRepository,
            IAuthUserRepository userRepository,
            ICacheService cacheService,
            IAuthNotificationService authNotificationService,
            ClientRequestInfo requestInfo,
            IDeviceTrustService deviceTrustService,
            IAuthSessionService authSessionService,
            ILoginStepUpChallengeService loginStepUpChallengeService,
            ITokenService tokenService)
        {
            _deviceRepository = deviceRepository;
            _userRepository = userRepository;
            _cacheService = cacheService;
            _authNotificationService = authNotificationService;
            _requestInfo = requestInfo;
            _deviceTrustService = deviceTrustService;
            _authSessionService = authSessionService;
            _loginStepUpChallengeService = loginStepUpChallengeService;
            _tokenService = tokenService;
        }

        public async Task EnsureDeviceKnownAsync(int userId, string userEmail, ClientRequestInfo requestInfo, string? returnUrl = null)
        {
            try
            {
                if (await _deviceTrustService.IsTrustedAsync(userId, requestInfo))
                    return;

                var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
                var trustedDeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

                var pendingDevice = new PendingDevice
                {
                    UserId = userId,
                    Email = userEmail,
                    DeviceType = requestInfo.DeviceType,
                    ClientName = requestInfo.ClientName,
                    IpAddress = requestInfo.IpAddress,
                    TrustedDeviceId = trustedDeviceId,
                    ReturnPath = returnUrl
                };

                var serialized = JsonConvert.SerializeObject(pendingDevice);

                var stored = await _cacheService.SetValueAsync(
                    key: $"device:pending:{token}",
                    value: serialized,
                    expiry: _pendingTtl
                );

                if (!stored)
                    throw new InternalServerErrorException("Device verification could not be initiated. Please try again.");

                await _authNotificationService.SendDeviceVerificationAsync(userEmail, token);

                throw new DeviceVerificationRequiredException();
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[DeviceService] EnsureDeviceKnownAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifyDeviceAsync(
            string token,
            SessionTransport transport
        )
        {
            try
            {
                var stepUpResult = await _loginStepUpChallengeService.TryVerifyEmailAsync(token);
                if (stepUpResult != null)
                    return stepUpResult;

                var json = await _cacheService.GetValueAsync($"device:pending:{token}");
                if (string.IsNullOrEmpty(json))
                    throw new UnauthorizedException("Invalid or expired device verification token.");

                var pending = JsonConvert.DeserializeObject<PendingDevice>(json)
                    ?? throw new UnauthorizedException("Invalid device verification token payload.");

                await _cacheService.DeleteKeyAsync($"device:pending:{token}");

                var user = await _userRepository.GetUserAsync(pending.UserId)
                    ?? throw new ResourceNotFoundException($"User with ID {pending.UserId} not found.");
                if (user.IsDisabled)
                    throw new ForbiddenException("This account is disabled.");

                var userToken = await _authSessionService.IssueAsync(user, transport, rememberMe: false);

                // Trust outlives the session - it lets the device skip verification for 90 days -
                // so it is written only if the account is still at the version the session was
                // issued under, compared under the account's row lock. A rotation committing since
                // the read above has already run its revocation, which this session missed.
                var trusted = await _deviceTrustService.TrustAsync(
                    pending.UserId,
                    user.AuthVersion,
                    pending.TrustedDeviceId,
                    pending.DeviceType,
                    pending.ClientName,
                    pending.IpAddress
                );
                if (!trusted)
                {
                    await _tokenService.RevokeAllRefreshSessionsAsync(pending.UserId);
                    throw new UnauthorizedException(
                        "This device verification link is no longer valid. Please sign in again.");
                }

                return new AuthenticatedSessionResult
                {
                    UserToken = userToken,
                    ReturnPath = pending.ReturnPath
                };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[DeviceService] VerifyDeviceAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private sealed class PendingDevice
        {
            public int UserId
            {
                get; set;
            }
            public required string Email
            {
                get; set;
            }
            public required string DeviceType
            {
                get; set;
            }
            public required string ClientName
            {
                get; set;
            }
            public required string TrustedDeviceId
            {
                get; set;
            }
            public string IpAddress { get; set; } = "Unknown";
            public string? ReturnPath
            {
                get; set;
            }
        }
    }
}
