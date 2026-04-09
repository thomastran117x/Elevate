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
        private readonly IDeviceRepository _deviceRepository;
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly ICacheService _cacheService;
        private readonly IPublisher _publisher;
        private readonly ClientRequestInfo _requestInfo;
        private readonly TimeSpan PENDING_TTL = TimeSpan.FromMinutes(15);

        public DeviceService(
            IDeviceRepository deviceRepository,
            IUserRepository userRepository,
            ITokenService tokenService,
            ICacheService cacheService,
            IPublisher publisher,
            ClientRequestInfo requestInfo)
        {
            _deviceRepository = deviceRepository;
            _userRepository = userRepository;
            _tokenService = tokenService;
            _cacheService = cacheService;
            _publisher = publisher;
            _requestInfo = requestInfo;
        }

        public async Task EnsureDeviceKnownAsync(int userId, string userEmail, ClientRequestInfo requestInfo)
        {
            try
            {
                var device = await _deviceRepository.GetDeviceAsync(
                    userId,
                    requestInfo.DeviceType,
                    requestInfo.ClientName
                );

                if (device != null)
                {
                    device.IpAddress = requestInfo.IpAddress;
                    await _deviceRepository.UpdateLastSeenAsync(device);
                    return;
                }

                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

                var pendingDevice = new PendingDevice
                {
                    UserId = userId,
                    Email = userEmail,
                    DeviceType = requestInfo.DeviceType,
                    ClientName = requestInfo.ClientName,
                    IpAddress = requestInfo.IpAddress
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

        public async Task<UserToken> VerifyDeviceAsync(string token)
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
                    DeviceType = pending.DeviceType,
                    ClientName = pending.ClientName,
                    IpAddress = pending.IpAddress
                });

                var user = await _userRepository.GetUserAsync(pending.UserId)
                    ?? throw new ResourceNotFoundException($"User with ID {pending.UserId} not found.");

                var accessToken = _tokenService.GenerateAccessToken(user);
                var refreshToken = await _tokenService.GenerateRefreshToken(user.Id, _requestInfo);
                var authToken = new Token(accessToken, refreshToken);

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
            public string IpAddress { get; set; } = "Unknown";
        }
    }
}
