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
        private readonly string JWT_VERIFICATION_SECRET;
        private readonly TimeSpan JWT_ACCESS_LIFETIME = TimeSpan.FromMinutes(15);
        private const string ISSUER = "EventXperience";
        private const string AUDIENCE = "EventXperienceConsumers";
        private const string VERIFICATION_AUDIENCE = "EventXperienceVerification";
        private readonly ICacheService _cacheService;
        private readonly TimeSpan REFRESH_TTL = TimeSpan.FromDays(7);
        private readonly TimeSpan VERIFY_TTL = TimeSpan.FromMinutes(30);

        public TokenService(ICacheService cacheService)
        {
            JWT_ACCESS_SECRET = EnvironmentSetting.JwtSecretKeyAccess;
            JWT_VERIFICATION_SECRET = EnvironmentSetting.JwtSecretKeyVerification;
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
                string nonce = Guid.NewGuid().ToString("N");

                var challenge = BuildVerificationChallenge(user, purpose, otpCode, expiresAtUtc, nonce);
                var payload = BuildVerificationPayload(user, purpose);

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
                    ExpiresAtUtc = expiresAtUtc,
                };

                var stateStored = await _cacheService.SetValueAsync(
                    key: VerificationStateKey(user.Email, purpose),
                    value: JsonConvert.SerializeObject(state),
                    expiry: VERIFY_TTL
                );

                if (!linkStored || !stateStored)
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
                _ = await _cacheService.DeleteKeyAsync(VerificationStateKey(payload.Email, payload.Purpose));

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
                var payload = ReadVerificationChallenge(challenge);
                if (payload.Purpose != expectedPurpose)
                    throw new UnauthorizedException("Verification challenge purpose mismatch.");

                var expectedProof = ComputeOtpProof(
                    payload.Purpose,
                    payload.Email,
                    payload.Password,
                    payload.Usertype,
                    payload.ExpiresAtUtc,
                    payload.Nonce,
                    code
                );

                if (!FixedTimeEquals(payload.OtpProof, expectedProof))
                    throw new UnauthorizedException("Invalid or expired verification code.");

                var state = await GetVerificationStateAsync(payload.Email, payload.Purpose);
                if (state != null && state.OtpChallenge == challenge)
                {
                    _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                    _ = await _cacheService.DeleteKeyAsync(VerificationStateKey(payload.Email, payload.Purpose));
                }

                return CreateUserFromPayload(new VerificationTokenPayload
                {
                    Email = payload.Email,
                    Password = payload.Password,
                    Usertype = payload.Usertype ?? "placeholder",
                    Purpose = payload.Purpose,
                });
            }
            catch (SecurityTokenException)
            {
                throw new UnauthorizedException("Invalid or expired verification challenge.");
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

        private VerificationTokenPayload BuildVerificationPayload(
            User user,
            VerificationPurpose purpose
        )
        {
            return new VerificationTokenPayload
            {
                Email = user.Email,
                Password = purpose == VerificationPurpose.SignUp ? user.Password : null,
                Usertype = purpose == VerificationPurpose.SignUp ? user.Usertype : "placeholder",
                Purpose = purpose,
            };
        }

        private string BuildVerificationChallenge(
            User user,
            VerificationPurpose purpose,
            string otpCode,
            DateTime expiresAtUtc,
            string nonce
        )
        {
            var payload = BuildVerificationPayload(user, purpose);
            var proof = ComputeOtpProof(
                purpose,
                payload.Email,
                payload.Password,
                payload.Usertype,
                expiresAtUtc,
                nonce,
                otpCode
            );

            var claims = new List<Claim>
            {
                new("purpose", purpose.ToString()),
                new("email", payload.Email),
                new("otp_proof", proof),
                new("nonce", nonce),
            };

            if (!string.IsNullOrWhiteSpace(payload.Password))
                claims.Add(new Claim("password", payload.Password));

            if (!string.IsNullOrWhiteSpace(payload.Usertype))
                claims.Add(new Claim("usertype", payload.Usertype));

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = expiresAtUtc,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JWT_VERIFICATION_SECRET)),
                    SecurityAlgorithms.HmacSha256Signature
                ),
                Issuer = ISSUER,
                Audience = VERIFICATION_AUDIENCE,
            };

            var token = _tokenHandler.CreateToken(descriptor);
            return _tokenHandler.WriteToken(token);
        }

        private VerificationChallengePayload ReadVerificationChallenge(string challenge)
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(JWT_VERIFICATION_SECRET)
                ),
                ValidateIssuer = true,
                ValidIssuer = ISSUER,
                ValidateAudience = true,
                ValidAudience = VERIFICATION_AUDIENCE,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };

            var principal = _tokenHandler.ValidateToken(challenge, parameters, out var validatedToken);
            var jwt = validatedToken as JwtSecurityToken
                ?? throw new SecurityTokenException("Invalid verification challenge.");

            var purposeValue = principal.FindFirst("purpose")?.Value
                ?? throw new SecurityTokenException("Missing verification purpose.");

            if (!Enum.TryParse<VerificationPurpose>(purposeValue, ignoreCase: true, out var purpose))
                throw new SecurityTokenException("Invalid verification purpose.");

            var email = principal.FindFirst("email")?.Value
                ?? throw new SecurityTokenException("Missing verification email.");

            var otpProof = principal.FindFirst("otp_proof")?.Value
                ?? throw new SecurityTokenException("Missing OTP proof.");

            var nonce = principal.FindFirst("nonce")?.Value
                ?? throw new SecurityTokenException("Missing OTP nonce.");

            return new VerificationChallengePayload
            {
                Purpose = purpose,
                Email = email,
                Password = principal.FindFirst("password")?.Value,
                Usertype = principal.FindFirst("usertype")?.Value,
                OtpProof = otpProof,
                Nonce = nonce,
                ExpiresAtUtc = jwt.ValidTo,
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

        private User CreateUserFromPayload(VerificationTokenPayload payload)
        {
            return new User
            {
                Email = payload.Email,
                Password = payload.Password,
                Usertype = payload.Usertype,
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
            string nonce,
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
                nonce,
                otpCode
            );

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(JWT_VERIFICATION_SECRET));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(material)));
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static string TokenKey(string tokenHash) => $"refresh:token:{tokenHash}";

        private static string SessionKey(string sessionId) => $"refresh:session:{sessionId}";

        private static string UserSessionsKey(int userId) => $"refresh:user:{userId}:sessions";

        private static string VerificationTokenKey(string token) => $"verify:token:{token}";

        private static string VerificationStateKey(string email, VerificationPurpose purpose) =>
            $"verify:email:{purpose}:{email}";

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
            public DateTime ExpiresAtUtc { get; set; }
        }
    }
}
