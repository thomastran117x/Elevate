using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using backend.main.configurations.environment;
using backend.main.dtos.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.services.implementation
{
    public class TokenService : ITokenService
    {
        private readonly JwtSecurityTokenHandler _tokenHandler = new();
        private readonly string JWT_ACCESS_SECRET;
        private readonly TimeSpan JWT_ACCESS_LIFETIME = TimeSpan.FromMinutes(15);
        private const string ISSUER = "EventXperience";
        private const string AUDIENCE = "EventXperienceConsumers";
        private readonly ICacheService _cacheService;
        private readonly TimeSpan REFRESH_TTL = TimeSpan.FromDays(7);
        private readonly TimeSpan VERIFY_TTL = TimeSpan.FromMinutes(30);

        public TokenService(ICacheService cacheService)
        {
            JWT_ACCESS_SECRET = EnvironmentSetting.JwtSecretKeyAccess;
            _cacheService = cacheService;
        }

        public string GenerateAccessToken(User user)
        {
            try
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWT_ACCESS_SECRET));

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Email),
                    new Claim(ClaimTypes.Role, user.Usertype),
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.Add(JWT_ACCESS_LIFETIME),
                    SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
                    Issuer = ISSUER,
                    Audience = AUDIENCE
                };

                var token = _tokenHandler.CreateToken(tokenDescriptor);
                return _tokenHandler.WriteToken(token);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateAccessToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<string> GenerateRefreshToken(
            int userId,
            ClientRequestInfo requestInfo,
            string? sessionId = null
        )
        {
            try
            {
                string token;
                string tokenHash;

                do
                {
                    token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    tokenHash = ComputeTokenHash(token);

                    string? existing = await _cacheService.GetValueAsync(TokenKey(tokenHash));

                    if (existing == null)
                        break;
                }

                while (true);

                RefreshSessionState session;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    session = new RefreshSessionState
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        UserId = userId,
                        DeviceType = requestInfo.DeviceType,
                        ClientName = requestInfo.ClientName,
                        IsBrowserClient = requestInfo.IsBrowserClient,
                        LastSeenIpAddress = requestInfo.IpAddress,
                        CreatedAt = DateTime.UtcNow,
                        LastSeenAt = DateTime.UtcNow,
                        CurrentTokenHash = tokenHash,
                    };
                }
                else
                {
                    session = await GetRefreshSessionAsync(sessionId)
                        ?? throw new UnauthorizedException("Refresh session is invalid or expired.");

                    if (session.UserId != userId)
                        throw new UnauthorizedException("Refresh session user mismatch.");

                    session.DeviceType = requestInfo.DeviceType;
                    session.ClientName = requestInfo.ClientName;
                    session.IsBrowserClient = requestInfo.IsBrowserClient;
                    session.LastSeenIpAddress = requestInfo.IpAddress;
                    session.LastSeenAt = DateTime.UtcNow;
                    session.CurrentTokenHash = tokenHash;
                }

                var tokenRecord = new RefreshTokenRecord
                {
                    SessionId = session.SessionId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                };

                var tokenResult = await _cacheService.SetValueAsync(
                    key: TokenKey(tokenHash),
                    value: JsonConvert.SerializeObject(tokenRecord),
                    expiry: REFRESH_TTL
                );

                var sessionResult = await _cacheService.SetValueAsync(
                    key: SessionKey(session.SessionId),
                    value: JsonConvert.SerializeObject(session),
                    expiry: REFRESH_TTL
                );

                await _cacheService.SetAddAsync(UserSessionsKey(userId), session.SessionId);

                if (!tokenResult || !sessionResult)
                    throw new NotAvailableException();

                return token;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateRefreshToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<RefreshTokenValidationResult> ValidateRefreshToken(
            string refreshToken,
            ClientRequestInfo requestInfo
        )
        {
            try
            {
                var tokenHash = ComputeTokenHash(refreshToken);
                string? storedValue = await _cacheService.GetValueAsync(TokenKey(tokenHash));

                if (string.IsNullOrEmpty(storedValue))
                    throw new UnauthorizedException("Invalid or expired refresh token.");

                var tokenRecord = JsonConvert.DeserializeObject<RefreshTokenRecord>(storedValue)
                    ?? throw new UnauthorizedException("Invalid refresh token payload.");

                var session = await GetRefreshSessionAsync(tokenRecord.SessionId)
                    ?? throw new UnauthorizedException("Refresh session is invalid or expired.");

                if (session.UserId != tokenRecord.UserId)
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Refresh session user mismatch.");
                }

                if (session.CurrentTokenHash != tokenHash)
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Refresh token reuse detected.");
                }

                if (!IsRequestCompatible(session, requestInfo))
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Refresh request does not match the active session.");
                }

                var result = await _cacheService.DeleteKeyAsync(TokenKey(tokenHash));
                if (!result)
                    throw new NotAvailableException();

                session.LastSeenAt = DateTime.UtcNow;
                session.LastSeenIpAddress = requestInfo.IpAddress;

                var sessionUpdated = await _cacheService.SetValueAsync(
                    SessionKey(session.SessionId),
                    JsonConvert.SerializeObject(session),
                    REFRESH_TTL
                );

                if (!sessionUpdated)
                    throw new NotAvailableException();

                return new RefreshTokenValidationResult
                {
                    SessionId = session.SessionId,
                    UserId = tokenRecord.UserId,
                };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] ValidateRefreshToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<string> GenerateVerificationToken(User user)
        {
            try
            {
                var existingToken = await _cacheService.GetValueAsync($"verify:email:{user.Email}");
                if (existingToken is not null)
                    return existingToken;

                string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

                var serialized = JsonConvert.SerializeObject(new User
                {
                    Email = user.Email,
                    Password = user.Password,
                    Usertype = user.Usertype
                });

                var result = await _cacheService.SetValueAsync(
                    key: $"verify:token:{token}",
                    value: serialized,
                    expiry: VERIFY_TTL
                );

                _ = await _cacheService.SetValueAsync(
                    key: $"verify:email:{user.Email}",
                    value: token,
                    expiry: VERIFY_TTL
                );

                if (!result)
                    throw new NotAvailableException();

                return token;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateVerificationToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<User> VerifyVerificationToken(string token)
        {
            try
            {
                string? json = await _cacheService.GetValueAsync($"verify:token:{token}");

                if (string.IsNullOrEmpty(json))
                    throw new UnauthorizedException("Invalid or expired verification token.");

                var draft = JsonConvert.DeserializeObject<User>(json)
                    ?? throw new UnauthorizedException("Invalid verification token payload.");

                _ = await _cacheService.DeleteKeyAsync($"verify:token:{token}");
                _ = await _cacheService.DeleteKeyAsync($"verify:email:{draft.Email}");

                return draft;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] VerifyVerificationToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<string?> VerificationTokenExist(string email)
        {
            try
            {
                var existingToken = await _cacheService.GetValueAsync($"verify:email:{email}");
                if (existingToken == null)
                {
                    return null;
                }
                else
                {
                    return existingToken;
                }
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] VerificationTokenExist failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RevokeRefreshSessionAsync(string sessionId)
        {
            try
            {
                var session = await GetRefreshSessionAsync(sessionId);
                if (session == null)
                    return;

                if (!string.IsNullOrWhiteSpace(session.CurrentTokenHash))
                    await _cacheService.DeleteKeyAsync(TokenKey(session.CurrentTokenHash));

                await _cacheService.DeleteKeyAsync(SessionKey(session.SessionId));
                await _cacheService.SetRemoveAsync(UserSessionsKey(session.UserId), session.SessionId);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] RevokeRefreshSessionAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RevokeAllRefreshSessionsAsync(int userId)
        {
            try
            {
                var sessionIds = await _cacheService.SetMembersAsync(UserSessionsKey(userId));
                foreach (var sessionId in sessionIds)
                    await RevokeRefreshSessionAsync(sessionId);

                await _cacheService.DeleteKeyAsync(UserSessionsKey(userId));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] RevokeAllRefreshSessionsAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task<RefreshSessionState?> GetRefreshSessionAsync(string sessionId)
        {
            var json = await _cacheService.GetValueAsync(SessionKey(sessionId));
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonConvert.DeserializeObject<RefreshSessionState>(json);
        }

        private static bool IsRequestCompatible(
            RefreshSessionState session,
            ClientRequestInfo requestInfo
        )
        {
            return session.DeviceType == requestInfo.DeviceType
                && session.ClientName == requestInfo.ClientName
                && session.IsBrowserClient == requestInfo.IsBrowserClient;
        }

        private static string ComputeTokenHash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private static string TokenKey(string tokenHash) => $"refresh:token:{tokenHash}";

        private static string SessionKey(string sessionId) => $"refresh:session:{sessionId}";

        private static string UserSessionsKey(int userId) => $"refresh:user:{userId}:sessions";

        private sealed class RefreshTokenRecord
        {
            public required string SessionId { get; set; }
            public int UserId { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private sealed class RefreshSessionState
        {
            public required string SessionId { get; set; }
            public int UserId { get; set; }
            public required string DeviceType { get; set; }
            public required string ClientName { get; set; }
            public bool IsBrowserClient { get; set; }
            public required string CurrentTokenHash { get; set; }
            public string LastSeenIpAddress { get; set; } = "Unknown";
            public DateTime CreatedAt { get; set; }
            public DateTime LastSeenAt { get; set; }
        }
    }
}
