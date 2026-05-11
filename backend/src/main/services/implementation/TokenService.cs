using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.application.security;
using backend.main.dtos.general;
using backend.main.shared.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;
using backend.main.features.cache;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.services.implementation
{
    public class TokenService : ITokenService
    {
        public const string AuthVersionClaimType = "auth_version";
        private readonly JwtSecurityTokenHandler _tokenHandler = new();
        private readonly string JWT_ACCESS_SECRET;
        private readonly string JWT_VERIFICATION_SECRET;
        private readonly TimeSpan JWT_ACCESS_LIFETIME = TimeSpan.FromMinutes(15);
        private const string ISSUER = "EventXperience";
        private const string AUDIENCE = "EventXperienceConsumers";
        private const string VERIFICATION_AUDIENCE = "EventXperienceVerification";
        private readonly ICacheService _cacheService;
        private const string RefreshKeyPrefix = "refresh:v2";
        private readonly TimeSpan DEFAULT_REFRESH_TTL = TimeSpan.FromDays(1);
        private readonly TimeSpan REMEMBERED_REFRESH_TTL = TimeSpan.FromDays(30);
        private readonly TimeSpan VERIFY_TTL = TimeSpan.FromMinutes(30);
        private const int MAX_OTP_ATTEMPTS = 5;

        public TokenService(ICacheService cacheService)
        {
            JWT_ACCESS_SECRET = EnvironmentSetting.JwtSecretKeyAccess;
            JWT_VERIFICATION_SECRET = EnvironmentSetting.JwtSecretKeyVerification;
            _cacheService = cacheService;
        }

        public AccessTokenIssue GenerateAccessToken(User user)
        {
            try
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWT_ACCESS_SECRET));
                var expiresAtUtc = DateTime.UtcNow.Add(JWT_ACCESS_LIFETIME);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Email),
                    new Claim(ClaimTypes.Role, AuthRoles.NormalizeStored(user.Usertype)),
                    new Claim(AuthVersionClaimType, user.AuthVersion.ToString()),
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = expiresAtUtc,
                    SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature),
                    Issuer = ISSUER,
                    Audience = AUDIENCE
                };

                var token = _tokenHandler.CreateToken(tokenDescriptor);
                return new AccessTokenIssue(_tokenHandler.WriteToken(token), expiresAtUtc);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateAccessToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<RefreshTokenIssue> GenerateRefreshToken(
            int userId,
            ClientRequestInfo requestInfo,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        )
        {
            try
            {
                string refreshToken;
                string refreshTokenHash;
                string sessionBindingToken;
                string sessionBindingTokenHash;
                TimeSpan refreshTtl;

                do
                {
                    refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                    refreshTokenHash = ComputeTokenHash(refreshToken);

                    string? existing = await _cacheService.GetValueAsync(TokenKey(refreshTokenHash));

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
                        Transport = transport,
                        LastSeenDeviceType = requestInfo.DeviceType,
                        LastSeenClientName = requestInfo.ClientName,
                        LastSeenIpAddress = requestInfo.IpAddress,
                        CreatedAt = DateTime.UtcNow,
                        LastSeenAt = DateTime.UtcNow,
                        CurrentRefreshTokenHash = string.Empty,
                        CurrentBindingTokenHash = string.Empty,
                        RememberMe = rememberMe ?? false,
                    };
                    refreshTtl = ResolveRefreshTtl(session.RememberMe);
                }
                else
                {
                    session = await GetRefreshSessionAsync(sessionId)
                        ?? throw new UnauthorizedException("Refresh session is invalid or expired.");

                    if (session.UserId != userId)
                        throw new UnauthorizedException("Refresh session user mismatch.");

                    if (session.Transport != transport)
                        throw new UnauthorizedException("Refresh session transport mismatch.");

                    session.LastSeenDeviceType = requestInfo.DeviceType;
                    session.LastSeenClientName = requestInfo.ClientName;
                    session.LastSeenIpAddress = requestInfo.IpAddress;
                    session.LastSeenAt = DateTime.UtcNow;
                    if (rememberMe.HasValue)
                        session.RememberMe = rememberMe.Value;

                    refreshTtl = ResolveRefreshTtl(session.RememberMe);
                }

                sessionBindingToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                sessionBindingTokenHash = ComputeTokenHash(sessionBindingToken);
                session.CurrentRefreshTokenHash = refreshTokenHash;
                session.CurrentBindingTokenHash = sessionBindingTokenHash;

                var tokenRecord = new RefreshTokenRecord
                {
                    SessionId = session.SessionId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                };

                var tokenResult = await _cacheService.SetValueAsync(
                    key: TokenKey(refreshTokenHash),
                    value: JsonConvert.SerializeObject(tokenRecord),
                    expiry: refreshTtl
                );

                var sessionResult = await _cacheService.SetValueAsync(
                    key: SessionKey(session.SessionId),
                    value: JsonConvert.SerializeObject(session),
                    expiry: refreshTtl
                );

                await _cacheService.SetAddAsync(UserSessionsKey(userId), session.SessionId);

                if (!tokenResult || !sessionResult)
                    throw new NotAvailableException();

                return new RefreshTokenIssue(
                    refreshToken,
                    sessionBindingToken,
                    refreshTtl,
                    session.Transport
                );
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
            string? sessionBindingToken,
            SessionTransport expectedTransport,
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

                if (session.Transport != expectedTransport)
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Refresh token transport mismatch.");
                }

                if (session.CurrentRefreshTokenHash != tokenHash)
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Refresh token reuse detected.");
                }

                if (string.IsNullOrWhiteSpace(sessionBindingToken))
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Missing session binding token.");
                }

                if (session.CurrentBindingTokenHash != ComputeTokenHash(sessionBindingToken))
                {
                    await RevokeRefreshSessionAsync(tokenRecord.SessionId);
                    throw new UnauthorizedException("Invalid session binding token.");
                }

                var result = await _cacheService.DeleteKeyAsync(TokenKey(tokenHash));
                if (!result)
                    throw new NotAvailableException();

                session.LastSeenAt = DateTime.UtcNow;
                session.LastSeenIpAddress = requestInfo.IpAddress;
                session.LastSeenClientName = requestInfo.ClientName;
                session.LastSeenDeviceType = requestInfo.DeviceType;

                var sessionUpdated = await _cacheService.SetValueAsync(
                    SessionKey(session.SessionId),
                    JsonConvert.SerializeObject(session),
                    ResolveRefreshTtl(session.RememberMe)
                );

                if (!sessionUpdated)
                    throw new NotAvailableException();

                return new RefreshTokenValidationResult
                {
                    SessionId = session.SessionId,
                    UserId = tokenRecord.UserId,
                    Transport = session.Transport,
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

        public async Task<string> GenerateVerificationToken(User user, VerificationPurpose purpose)
        {
            try
            {
                var artifacts = await GenerateVerificationArtifactsAsync(user, purpose);
                return artifacts.LinkToken;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateVerificationToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<VerificationOtpChallenge> GenerateVerificationOtpAsync(
            User user,
            VerificationPurpose purpose
        )
        {
            try
            {
                var artifacts = await GenerateVerificationArtifactsAsync(user, purpose);
                return artifacts.OtpChallenge;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateVerificationOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<VerificationArtifacts> GenerateVerificationArtifactsAsync(
            User user,
            VerificationPurpose purpose
        )
        {
            try
            {
                var existingState = await GetVerificationStateAsync(user.Email, purpose);
                if (existingState != null)
                {
                    return new VerificationArtifacts
                    {
                        LinkToken = existingState.LinkToken,
                        OtpChallenge = new VerificationOtpChallenge
                        {
                            Code = existingState.OtpCode,
                            Challenge = existingState.OtpChallenge,
                            ExpiresAtUtc = existingState.ExpiresAtUtc,
                        },
                        Purpose = existingState.Purpose,
                    };
                }

                string linkToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                string otpCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                DateTime expiresAtUtc = DateTime.UtcNow.Add(VERIFY_TTL);
                var payload = BuildVerificationPayload(user, purpose);
                string challenge = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
                var otpProof = ComputeOtpProof(
                    purpose,
                    payload.Email,
                    payload.Password,
                    payload.Usertype,
                    expiresAtUtc,
                    challenge,
                    otpCode
                );

                var linkStored = await _cacheService.SetValueAsync(
                    key: VerificationTokenKey(linkToken),
                    value: JsonConvert.SerializeObject(payload),
                    expiry: VERIFY_TTL
                );

                var state = new VerificationDeliveryState
                {
                    Email = user.Email,
                    Purpose = purpose,
                    LinkToken = linkToken,
                    OtpCode = otpCode,
                    OtpChallenge = challenge,
                    OtpProof = otpProof,
                    Password = payload.Password,
                    Usertype = payload.Usertype,
                    ExpiresAtUtc = expiresAtUtc,
                };

                var stateJson = JsonConvert.SerializeObject(state);

                var stateStored = await _cacheService.SetValueAsync(
                    key: VerificationStateKey(user.Email, purpose),
                    value: stateJson,
                    expiry: VERIFY_TTL
                );

                var challengeStored = await _cacheService.SetValueAsync(
                    key: VerificationChallengeKey(challenge),
                    value: stateJson,
                    expiry: VERIFY_TTL
                );

                if (!linkStored || !stateStored || !challengeStored)
                    throw new NotAvailableException();

                return new VerificationArtifacts
                {
                    LinkToken = linkToken,
                    OtpChallenge = new VerificationOtpChallenge
                    {
                        Code = otpCode,
                        Challenge = challenge,
                        ExpiresAtUtc = expiresAtUtc,
                    },
                    Purpose = purpose,
                };
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GenerateVerificationArtifactsAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<User> VerifyVerificationToken(string token, VerificationPurpose expectedPurpose)
        {
            try
            {
                string? json = await _cacheService.GetValueAsync(VerificationTokenKey(token));

                if (string.IsNullOrEmpty(json))
                    throw new UnauthorizedException("Invalid or expired verification token.");

                var payload = JsonConvert.DeserializeObject<VerificationTokenPayload>(json)
                    ?? throw new UnauthorizedException("Invalid verification token payload.");

                if (payload.Purpose != expectedPurpose)
                    throw new UnauthorizedException("Verification token purpose mismatch.");

                _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(token));
                var state = await GetVerificationStateAsync(payload.Email, payload.Purpose);
                if (state != null)
                    await DeleteVerificationStateAsync(state);
                else
                    _ = await _cacheService.DeleteKeyAsync(
                        VerificationStateKey(payload.Email, payload.Purpose)
                    );

                return CreateUserFromPayload(payload);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] VerifyVerificationToken failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<User> VerifyVerificationOtpAsync(
            string code,
            string challenge,
            VerificationPurpose expectedPurpose
        )
        {
            try
            {
                var state = await GetVerificationStateByChallengeAsync(challenge);
                if (state == null)
                    throw new UnauthorizedException("Invalid or expired verification challenge.");

                if (state.Purpose != expectedPurpose)
                    throw new UnauthorizedException("Verification challenge purpose mismatch.");

                var expectedProof = ComputeOtpProof(
                    state.Purpose,
                    state.Email,
                    state.Password,
                    state.Usertype,
                    state.ExpiresAtUtc,
                    challenge,
                    code
                );

                if (state.OtpChallenge != challenge)
                    throw new UnauthorizedException("Invalid or expired verification challenge.");

                if (!FixedTimeEquals(state.OtpProof, expectedProof))
                {
                    var attempts = await RecordFailedOtpAttemptAsync(challenge);
                    if (attempts >= MAX_OTP_ATTEMPTS)
                    {
                        _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                        await DeleteVerificationStateAsync(state);
                    }

                    throw new UnauthorizedException("Invalid or expired verification code.");
                }

                _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                await DeleteVerificationStateAsync(state);
                _ = await _cacheService.DeleteKeyAsync(OtpAttemptKey(challenge));

                return CreateUserFromPayload(new VerificationTokenPayload
                {
                    Email = state.Email,
                    Password = state.Password,
                    Usertype = state.Usertype,
                    Purpose = state.Purpose,
                });
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] VerifyVerificationOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<string?> VerificationTokenExist(string email, VerificationPurpose purpose)
        {
            try
            {
                var state = await GetVerificationStateAsync(email, purpose);
                return state?.LinkToken;
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

                if (!string.IsNullOrWhiteSpace(session.CurrentRefreshTokenHash))
                    await _cacheService.DeleteKeyAsync(TokenKey(session.CurrentRefreshTokenHash));

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

        private VerificationTokenPayload BuildVerificationPayload(
            User user,
            VerificationPurpose purpose
        )
        {
            return new VerificationTokenPayload
            {
                Email = user.Email,
                Password = purpose == VerificationPurpose.SignUp ? user.Password : null,
                Usertype = purpose == VerificationPurpose.SignUp
                    ? AuthRoles.NormalizeStored(user.Usertype)
                    : "placeholder",
                Purpose = purpose,
            };
        }

        private async Task<VerificationDeliveryState?> GetVerificationStateAsync(
            string email,
            VerificationPurpose purpose
        )
        {
            var json = await _cacheService.GetValueAsync(VerificationStateKey(email, purpose));
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonConvert.DeserializeObject<VerificationDeliveryState>(json);
        }

        private async Task<VerificationDeliveryState?> GetVerificationStateByChallengeAsync(
            string challenge
        )
        {
            var json = await _cacheService.GetValueAsync(VerificationChallengeKey(challenge));
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonConvert.DeserializeObject<VerificationDeliveryState>(json);
        }

        private async Task DeleteVerificationStateAsync(VerificationDeliveryState state)
        {
            _ = await _cacheService.DeleteKeyAsync(VerificationStateKey(state.Email, state.Purpose));
            _ = await _cacheService.DeleteKeyAsync(VerificationChallengeKey(state.OtpChallenge));
            _ = await _cacheService.DeleteKeyAsync(OtpAttemptKey(state.OtpChallenge));
        }

        private User CreateUserFromPayload(VerificationTokenPayload payload)
        {
            return new User
            {
                Email = payload.Email,
                Password = payload.Password,
                Usertype = AuthRoles.NormalizeStored(payload.Usertype),
            };
        }

        private static string ComputeTokenHash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }

        private string ComputeOtpProof(
            VerificationPurpose purpose,
            string email,
            string? password,
            string? usertype,
            DateTime expiresAtUtc,
            string challenge,
            string otpCode
        )
        {
            var material = string.Join(
                "|",
                purpose,
                email,
                password ?? string.Empty,
                usertype ?? string.Empty,
                expiresAtUtc.ToUniversalTime().Ticks,
                challenge,
                otpCode
            );

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(JWT_VERIFICATION_SECRET));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(material)));
        }

        private TimeSpan ResolveRefreshTtl(bool rememberMe) =>
            rememberMe ? REMEMBERED_REFRESH_TTL : DEFAULT_REFRESH_TTL;

        private async Task<long> RecordFailedOtpAttemptAsync(string challenge)
        {
            var key = OtpAttemptKey(challenge);
            var attempts = await _cacheService.IncrementAsync(key);
            _ = await _cacheService.SetExpiryAsync(key, VERIFY_TTL);
            return attempts;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            if (leftBytes.Length != rightBytes.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static string TokenKey(string tokenHash) =>
            $"{RefreshKeyPrefix}:token:{tokenHash}";

        private static string SessionKey(string sessionId) =>
            $"{RefreshKeyPrefix}:session:{sessionId}";

        private static string UserSessionsKey(int userId) =>
            $"{RefreshKeyPrefix}:user:{userId}:sessions";

        private static string VerificationTokenKey(string token) => $"verify:token:{token}";

        private static string VerificationStateKey(string email, VerificationPurpose purpose) =>
            $"verify:email:{purpose}:{email}";

        private static string VerificationChallengeKey(string challenge) =>
            $"verify:challenge:{challenge}";

        private static string OtpAttemptKey(string challenge) =>
            $"verify:otp-attempt:{challenge}";

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
            public SessionTransport Transport { get; set; }
            public required string CurrentRefreshTokenHash { get; set; }
            public required string CurrentBindingTokenHash { get; set; }
            public bool RememberMe { get; set; }
            public string LastSeenIpAddress { get; set; } = "Unknown";
            public string LastSeenClientName { get; set; } = "Unknown";
            public string LastSeenDeviceType { get; set; } = "Unknown";
            public DateTime CreatedAt { get; set; }
            public DateTime LastSeenAt { get; set; }
        }

        private sealed class VerificationTokenPayload
        {
            public required string Email { get; set; }
            public string? Password { get; set; }
            public required string Usertype { get; set; }
            public VerificationPurpose Purpose { get; set; }
        }

        private sealed class VerificationDeliveryState
        {
            public required string Email { get; set; }
            public VerificationPurpose Purpose { get; set; }
            public required string LinkToken { get; set; }
            public required string OtpCode { get; set; }
            public required string OtpChallenge { get; set; }
            public required string OtpProof { get; set; }
            public string? Password { get; set; }
            public required string Usertype { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
        }
    }
}
