using System.Security.Cryptography;

using backend.main.dtos;
using backend.main.dtos.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Newtonsoft.Json;

namespace backend.main.services.implementation
{
    public class DeviceService : IDeviceService
    {
        private static readonly TimeSpan TrustedDeviceLifetime = TimeSpan.FromDays(90);
        private readonly IDeviceRepository _deviceRepository;
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly ICacheService _cacheService;
        private readonly IPublisher _publisher;
        private readonly ClientRequestInfo _requestInfo;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TimeSpan PENDING_TTL = TimeSpan.FromMinutes(15);

        public DeviceService(
            IDeviceRepository deviceRepository,
            IUserRepository userRepository,
            ITokenService tokenService,
            ICacheService cacheService,
            IPublisher publisher,
            ClientRequestInfo requestInfo,
            IHttpContextAccessor httpContextAccessor)
        {
            _deviceRepository = deviceRepository;
            _userRepository = userRepository;
            _tokenService = tokenService;
            _cacheService = cacheService;
            _publisher = publisher;
            _requestInfo = requestInfo;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task EnsureDeviceKnownAsync(int userId, string userEmail, ClientRequestInfo requestInfo)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var trustedDeviceToken = httpContext == null
                    ? null
                    : HttpUtility.ResolveTrustedDeviceToken(httpContext.Request);

                if (!string.IsNullOrWhiteSpace(trustedDeviceToken))
                {
                    var device = await _deviceRepository.GetDeviceAsync(
                        userId,
                        ComputeDeviceTokenHash(trustedDeviceToken)
                    );

                    if (device != null)
                    {
                        device.IpAddress = requestInfo.IpAddress;
                        device.DeviceType = requestInfo.DeviceType;
                        device.ClientName = requestInfo.ClientName;
                        await _deviceRepository.UpdateLastSeenAsync(device);

                        if (httpContext != null)
                        {
                            HttpUtility.SetTrustedDeviceToken(
                                httpContext.Response,
                                requestInfo,
                                trustedDeviceToken,
                                TrustedDeviceLifetime
                            );
                        }

                        return;
                    }

                    if (httpContext != null)
                        HttpUtility.ClearTrustedDeviceToken(httpContext.Response, requestInfo);
                }

                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                var trustedDeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

                var pendingDevice = new PendingDevice
                {
                    UserId = userId,
                    Email = userEmail,
                    DeviceType = requestInfo.DeviceType,
                    ClientName = requestInfo.ClientName,
                    IpAddress = requestInfo.IpAddress,
                    TrustedDeviceId = trustedDeviceId
                };

                var serialized = JsonConvert.SerializeObject(pendingDevice);

                await _cacheService.SetValueAsync(
                    key: $"device:pending:{token}",
                    value: serialized,
                    expiry: PENDING_TTL
                );

                var message = new EmailMessage
                {
                    Type = EmailMessageType.NewDevice,
                    Email = userEmail,
                    Token = token
                };

                await _publisher.PublishAsync("eventxperience-email", message);

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

        public async Task<UserToken> VerifyDeviceAsync(
            string token,
            SessionTransport transport
        )
        {
            try
            {
                var json = await _cacheService.GetValueAsync($"device:pending:{token}");

                if (string.IsNullOrEmpty(json))
                    throw new UnauthorizedException("Invalid or expired device verification token.");

                var pending = JsonConvert.DeserializeObject<PendingDevice>(json)
                    ?? throw new UnauthorizedException("Invalid device verification token payload.");

                await _cacheService.DeleteKeyAsync($"device:pending:{token}");

                await _deviceRepository.CreateDeviceAsync(new Device
                {
                    UserId = pending.UserId,
                    DeviceTokenHash = ComputeDeviceTokenHash(pending.TrustedDeviceId),
                    DeviceType = pending.DeviceType,
                    ClientName = pending.ClientName,
                    IpAddress = pending.IpAddress
                });

                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    HttpUtility.SetTrustedDeviceToken(
                        httpContext.Response,
                        _requestInfo,
                        pending.TrustedDeviceId,
                        TrustedDeviceLifetime
                    );
                }

                var user = await _userRepository.GetUserAsync(pending.UserId)
                    ?? throw new ResourceNotFoundException($"User with ID {pending.UserId} not found.");
                if (user.IsDisabled)
                    throw new ForbiddenException("This account is disabled.");

                var accessToken = _tokenService.GenerateAccessToken(user);
                var refreshToken = await _tokenService.GenerateRefreshToken(
                    user.Id,
                    _requestInfo,
                    transport,
                    sessionId: null,
                    rememberMe: false
                );
                var authToken = new Token(
                    accessToken.Value,
                    accessToken.ExpiresAtUtc,
                    refreshToken.Value,
                    refreshToken.SessionBindingToken,
                    refreshToken.Lifetime,
                    refreshToken.Transport
                );

                return new UserToken(authToken, user);
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
            public int UserId { get; set; }
            public required string Email { get; set; }
            public required string DeviceType { get; set; }
            public required string ClientName { get; set; }
            public required string TrustedDeviceId { get; set; }
            public string IpAddress { get; set; } = "Unknown";
        }

        private static string ComputeDeviceTokenHash(string deviceToken)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(deviceToken));
            return Convert.ToHexString(bytes);
        }
    }
}
