using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using backend.main.application.environment;
using backend.main.application.security;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa.session;
using backend.main.features.auth.sessions;
using backend.main.features.cache;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.utilities;
using backend.main.shared.utilities.logger;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.features.auth.token
{
    public class TokenService : ITokenService
    {
        public const string AuthVersionClaimType = "auth_version";
        public const string SessionIdClaimType = "sid";
        private readonly JwtSecurityTokenHandler _tokenHandler = new();
        private readonly JwtSigningKeyRing _signingKeys;
        private readonly string JWT_VERIFICATION_SECRET;
        private readonly TimeSpan JWT_ACCESS_LIFETIME = TimeSpan.FromMinutes(15);
        private const string ISSUER = "EventXperience";
        private const string AUDIENCE = "EventXperienceConsumers";
        private const string VERIFICATION_AUDIENCE = "EventXperienceVerification";
        private readonly ICacheService _cacheService;
        private const string RefreshKeyPrefix = "refresh:v2";
        private const string RefreshTokenFormat = "v1";
        private readonly TimeSpan DEFAULT_REFRESH_TTL = TimeSpan.FromDays(1);
        private readonly TimeSpan REMEMBERED_REFRESH_TTL = TimeSpan.FromDays(30);
        // How long after rotation a replay of the retired token is treated as a harmless
        // duplicate (two tabs restoring at once, a network retry) rather than as theft.
        private static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(10);
        private readonly TimeSpan VERIFY_TTL = TimeSpan.FromMinutes(30);
        private const int MAX_OTP_ATTEMPTS = 5;
        private const string PlaceholderUsertype = "placeholder";
        private static readonly TimeSpan EmailChangeLockTtl = TimeSpan.FromSeconds(10);

        // Writes the proof only if the session key is still present and no factor change has
        // advanced the user's step-up generation since verification began. Revocation deletes
        // the session before the proof, so every interleaving ends with no proof for a dead
        // session; a factor change either lands first (write refused) or later (proof retired).
        private const string StoreStepUpProofScript = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
    return 0
end
if tonumber(redis.call('GET', KEYS[3]) or '0') ~= tonumber(ARGV[3]) then
    return 2
end
redis.call('SET', KEYS[2], ARGV[1], 'PX', ARGV[2])
return 1";

        // Replaces a value only if it is still exactly the one that was read. Refresh rotation
        // uses it so concurrent refreshes cannot both rotate, and so a session deleted by a
        // revocation in the meantime is never written back.
        // Lists the account's refresh sessions. A script, unlike the plain set read, reports when
        // the cache could not answer.
        private const string ReadSessionIdsScript = "return redis.call('SMEMBERS', KEYS[1])";

        private const string CompareAndSetScript = @"
if redis.call('GET', KEYS[1]) ~= ARGV[1] then
    return 0
end
redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
return 1";

        private readonly TimeProvider _timeProvider;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private readonly IAuthSessionRepository? _sessions;

        /// <param name="sessions">
        /// The authoritative session registry. Always supplied by the container; left out only by
        /// tests that exercise the Redis side alone.
        /// </param>
        /// <param name="signingKeys">
        /// The access-token keys. Supplied by the container; defaults to the environment's keys.
        /// </param>
        public TokenService(
            ICacheService cacheService,
            TimeProvider? timeProvider = null,
            IAuthSecurityEventEmitter? securityEvents = null,
            IAuthSessionRepository? sessions = null,
            JwtSigningKeyRing? signingKeys = null)
        {
            _signingKeys = signingKeys ?? JwtSigningKeyRing.FromEnvironment();
            JWT_VERIFICATION_SECRET = EnvironmentSetting.JwtSecretKeyVerification;
            _cacheService = cacheService;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _securityEvents = securityEvents ?? NullAuthSecurityEventEmitter.Instance;
            _sessions = sessions;
        }

        public AccessTokenIssue GenerateAccessToken(User user, string sessionId)
        {
            try
            {
                // The key carries its kid, which the handler writes into the token header.
                var key = _signingKeys.SigningKey;
                var expiresAtUtc = DateTime.UtcNow.Add(JWT_ACCESS_LIFETIME);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Email),
                    new Claim(ClaimTypes.Role, AuthRoles.NormalizeStored(user.Usertype)),
                    new Claim(AuthVersionClaimType, user.AuthVersion.ToString()),
                    new Claim(SessionIdClaimType, sessionId),
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
            int authVersion,
            ClientRequestInfo requestInfo,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        )
        {
            try
            {
                RefreshSessionState session;
                string? storedSession = null;
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    session = new RefreshSessionState
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        UserId = userId,
                        AuthVersion = authVersion,
                        Transport = transport,
                        TokenSecret = NewTokenSecret(),
                        Generation = 1,
                        LastSeenDeviceType = requestInfo.DeviceType,
                        LastSeenClientName = requestInfo.ClientName,
                        LastSeenIpAddress = requestInfo.IpAddress,
                        CreatedAt = DateTime.UtcNow,
                        LastSeenAt = DateTime.UtcNow,
                        CurrentRefreshTokenHash = string.Empty,
                        CurrentBindingTokenHash = string.Empty,
                        RememberMe = rememberMe ?? false,
                    };
                }
                else
                {
                    (var existing, storedSession) = await ReadRefreshSessionAsync(sessionId);
                    session = existing
                        ?? throw new UnauthorizedException("Refresh session is invalid or expired.");

                    if (session.UserId != userId)
                        throw new UnauthorizedException("Refresh session user mismatch.");

                    if (session.Transport != transport)
                        throw new UnauthorizedException("Refresh session transport mismatch.");

                    if (session.AuthVersion is int issuedVersion && issuedVersion != authVersion)
                        throw new UnauthorizedException("Refresh session is no longer valid.");

                    // Also stamps a session minted before the version was recorded.
                    session.AuthVersion = authVersion;
                    session.LastSeenDeviceType = requestInfo.DeviceType;
                    session.LastSeenClientName = requestInfo.ClientName;
                    session.LastSeenIpAddress = requestInfo.IpAddress;
                    session.LastSeenAt = DateTime.UtcNow;
                    if (rememberMe.HasValue)
                        session.RememberMe = rememberMe.Value;

                    // A session from before derived tokens moves onto them here. The validation
                    // that preceded this retired its stored token.
                    if (session.TokenSecret is null)
                    {
                        session.TokenSecret = NewTokenSecret();
                        session.Generation = 1;
                        session.CurrentRefreshTokenHash = string.Empty;
                    }
                }

                var refreshTtl = ResolveRefreshTtl(session.RememberMe);
                var sessionBindingToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                session.CurrentBindingTokenHash = ComputeTokenHash(sessionBindingToken);
                var sessionJson = JsonConvert.SerializeObject(session);

                if (storedSession is null)
                {
                    if (!await _cacheService.SetValueAsync(SessionKey(session.SessionId), sessionJson, refreshTtl))
                        throw new NotAvailableException();
                }
                // Written only over the exact session that was read, so a revocation landing in
                // between (a reuse, a logout) is never undone by recreating the session.
                else if (!await CompareAndSetAsync(SessionKey(session.SessionId), storedSession, sessionJson, refreshTtl))
                {
                    throw new UnauthorizedException("Refresh session is invalid or expired.");
                }

                await _cacheService.SetAddAsync(UserSessionsKey(userId), session.SessionId);

                await RecordSessionUseAsync(session, requestInfo, refreshTtl, isNew: storedSession is null);

                return new RefreshTokenIssue(
                    DeriveRefreshToken(session.SessionId, session.TokenSecret, session.Generation),
                    sessionBindingToken,
                    refreshTtl,
                    session.Transport,
                    session.SessionId
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
                if (!TryParseRefreshToken(refreshToken, out var sessionId, out var generation))
                    return await ValidateLegacyRefreshTokenAsync(refreshToken, sessionBindingToken, expectedTransport);

                var (session, storedSession) = await ReadRefreshSessionAsync(sessionId);

                // Only the session's own secret produces a matching token, so a valid one is
                // proof it was issued for this session, whichever generation it carries.
                if (session?.TokenSecret is null
                    || storedSession is null
                    || generation > session.Generation
                    || !CryptoHelper.FixedTimeEquals(
                        refreshToken,
                        DeriveRefreshToken(session.SessionId, session.TokenSecret, generation)))
                {
                    throw new UnauthorizedException("Invalid or expired refresh token.");
                }

                // Checked before the binding token: a replayed token is a theft signal whether
                // or not the binding cookie came with it. Only the generation just rotated away
                // can still be inside the grace window.
                if (generation < session.Generation)
                {
                    throw await RejectRetiredRefreshTokenAsync(
                        session,
                        generation == session.Generation - 1 ? session.RotatedAt : null);
                }

                if (session.Transport != expectedTransport)
                {
                    await RevokeRefreshSessionAsync(session.SessionId, AuthSecurityReason.TransportMismatch);
                    throw new UnauthorizedException("Refresh token transport mismatch.");
                }

                await RequireBindingTokenAsync(session, sessionBindingToken);

                // Advancing the generation is what retires the presented token. It is a
                // compare-and-set, so of two refreshes racing on one token exactly one rotates,
                // and a session revoked meanwhile is not written back.
                session.Generation++;
                session.RotatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                session.LastSeenAt = DateTime.UtcNow;
                session.LastSeenIpAddress = requestInfo.IpAddress;
                session.LastSeenClientName = requestInfo.ClientName;
                session.LastSeenDeviceType = requestInfo.DeviceType;

                if (!await CompareAndSetAsync(
                        SessionKey(session.SessionId),
                        storedSession,
                        JsonConvert.SerializeObject(session),
                        ResolveRefreshTtl(session.RememberMe)))
                {
                    throw new UnauthorizedException("Refresh token was already rotated.");
                }

                return new RefreshTokenValidationResult
                {
                    SessionId = session.SessionId,
                    UserId = session.UserId,
                    AuthVersion = session.AuthVersion,
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

        /// <summary>
        /// Validates a random token stored under its hash, the format issued before derived
        /// tokens. It can be removed once the longest refresh lifetime (30 days) has passed
        /// since derived tokens shipped. Its session moves onto derived tokens when the
        /// refresh that follows issues the next one.
        /// </summary>
        private async Task<RefreshTokenValidationResult> ValidateLegacyRefreshTokenAsync(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport expectedTransport
        )
        {
            var tokenHash = ComputeTokenHash(refreshToken);
            string? storedValue = await _cacheService.GetValueAsync(TokenKey(tokenHash));

            if (string.IsNullOrEmpty(storedValue))
                throw new UnauthorizedException("Invalid or expired refresh token.");

            var tokenRecord = JsonConvert.DeserializeObject<RefreshTokenRecord>(storedValue)
                ?? throw new UnauthorizedException("Invalid refresh token payload.");

            var session = await GetRefreshSessionAsync(tokenRecord.SessionId);

            if (tokenRecord.RetiredAt is DateTime retiredAt)
            {
                // Already revoked or expired: the retired record outlived its session.
                if (session == null || session.UserId != tokenRecord.UserId)
                    throw new UnauthorizedException("Invalid or expired refresh token.");

                throw await RejectRetiredRefreshTokenAsync(session, retiredAt);
            }

            if (session == null)
                throw new UnauthorizedException("Refresh session is invalid or expired.");

            if (session.UserId != tokenRecord.UserId)
            {
                await RevokeRefreshSessionAsync(tokenRecord.SessionId, AuthSecurityReason.UserMismatch);
                throw new UnauthorizedException("Refresh session user mismatch.");
            }

            if (session.Transport != expectedTransport)
            {
                await RevokeRefreshSessionAsync(tokenRecord.SessionId, AuthSecurityReason.TransportMismatch);
                throw new UnauthorizedException("Refresh token transport mismatch.");
            }

            if (session.CurrentRefreshTokenHash != tokenHash)
            {
                EmitRefreshTokenReused(session);
                await RevokeRefreshSessionAsync(tokenRecord.SessionId, AuthSecurityReason.TokenReuse);
                throw new UnauthorizedException("Refresh token reuse detected.");
            }

            await RequireBindingTokenAsync(session, sessionBindingToken);

            // The retired record is the only one this session will ever leave behind: the next
            // token is derived. It lives for the refresh lifetime, never less than the token had.
            tokenRecord.RetiredAt = _timeProvider.GetUtcNow().UtcDateTime;
            if (!await CompareAndSetAsync(
                    TokenKey(tokenHash),
                    storedValue,
                    JsonConvert.SerializeObject(tokenRecord),
                    ResolveRefreshTtl(session.RememberMe)))
            {
                throw new UnauthorizedException("Refresh token was already rotated.");
            }

            return new RefreshTokenValidationResult
            {
                SessionId = session.SessionId,
                UserId = tokenRecord.UserId,
                AuthVersion = session.AuthVersion,
                Transport = session.Transport,
            };
        }

        private async Task RequireBindingTokenAsync(RefreshSessionState session, string? sessionBindingToken)
        {
            if (string.IsNullOrWhiteSpace(sessionBindingToken))
            {
                await RevokeRefreshSessionAsync(session.SessionId, AuthSecurityReason.BindingMissing);
                throw new UnauthorizedException("Missing session binding token.");
            }

            if (session.CurrentBindingTokenHash != ComputeTokenHash(sessionBindingToken))
            {
                await RevokeRefreshSessionAsync(session.SessionId, AuthSecurityReason.BindingMismatch);
                throw new UnauthorizedException("Invalid session binding token.");
            }
        }

        /// <summary>
        /// Handles a genuine refresh token that was already rotated away. Past the grace window
        /// this is reuse: whoever holds the newest token may be an attacker who refreshed first,
        /// so the whole session is revoked and both parties have to sign in again.
        /// </summary>
        /// <param name="session">The live session the token was issued for.</param>
        /// <param name="retiredAt">When the token was rotated away, or null when that was long
        /// enough ago that it cannot be a duplicate.</param>
        /// <returns>The rejection to throw. Throws itself if the revocation cannot be confirmed.</returns>
        private async Task<UnauthorizedException> RejectRetiredRefreshTokenAsync(
            RefreshSessionState session,
            DateTime? retiredAt
        )
        {
            if (retiredAt is DateTime rotated
                && _timeProvider.GetUtcNow().UtcDateTime - rotated < RefreshReuseGrace)
            {
                return new UnauthorizedException("Refresh token was already rotated.");
            }

            EmitRefreshTokenReused(session);
            await RevokeRefreshSessionAsync(session.SessionId, AuthSecurityReason.TokenReuse);
            return new UnauthorizedException("Refresh token reuse detected.");
        }

        /// <summary>Records a rejected verification link or code and returns the 401 to throw.</summary>
        private UnauthorizedException VerificationRejected(
            VerificationPurpose purpose,
            AuthSecurityReason reason,
            string message,
            int? userId = null,
            string? email = null)
        {
            _securityEvents.Emit(
                new AuthSecurityEvent
                {
                    Definition = AuthSecurityEvents.VerificationFailed,
                    Outcome = AuthSecurityOutcome.Denied,
                    Reason = reason,
                    Method = AuthSecurityMethod.Email,
                    Flow = FlowFor(purpose),
                    UserId = userId,
                },
                email is null ? null : AuthSecurityTarget.Email(email));

            return new UnauthorizedException(message);
        }

        internal static AuthSecurityFlow FlowFor(VerificationPurpose purpose) => purpose switch
        {
            VerificationPurpose.SignUp => AuthSecurityFlow.Signup,
            VerificationPurpose.ResetPassword => AuthSecurityFlow.PasswordReset,
            VerificationPurpose.ChangeEmail => AuthSecurityFlow.EmailChange,
            _ => AuthSecurityFlow.Verification,
        };

        private void EmitRefreshTokenReused(RefreshSessionState session) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.RefreshTokenReused,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = AuthSecurityReason.Replayed,
                Method = AuthSecurityMethod.RefreshToken,
                UserId = session.UserId,
                SessionId = session.SessionId,
            });

        /// <returns>False when the key no longer holds <paramref name="expected"/>.</returns>
        /// <exception cref="NotAvailableException">The cache could not run the swap.</exception>
        private async Task<bool> CompareAndSetAsync(string key, string expected, string replacement, TimeSpan ttl)
        {
            var result = await _cacheService.TryEvalAsync(
                CompareAndSetScript,
                [(StackExchange.Redis.RedisKey)key],
                [expected, replacement, checked((long)ttl.TotalMilliseconds)]
            );

            if (!result.Succeeded || result.Value is null)
                throw new NotAvailableException();

            return ToLong(result.Value) == 1;
        }

        private static long ToLong(object value) =>
            value is StackExchange.Redis.RedisResult redisResult
                ? (long)redisResult
                : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

        private static string NewTokenSecret() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        /// <summary>
        /// A refresh token is <c>v1.{sessionId}.{generation}.{mac}</c>, the MAC keyed by a secret
        /// that never leaves the session record. Every generation the session has issued can be
        /// recomputed, so a replayed token is recognised without storing anything per token.
        /// </summary>
        private static string DeriveRefreshToken(string sessionId, string tokenSecret, long generation)
        {
            using var hmac = new HMACSHA256(Convert.FromBase64String(tokenSecret));
            var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{sessionId}.{generation}"));
            return $"{RefreshTokenFormat}.{sessionId}.{generation}.{Base64UrlEncoder.Encode(mac)}";
        }

        private static bool TryParseRefreshToken(string refreshToken, out string sessionId, out long generation)
        {
            sessionId = string.Empty;
            generation = 0;

            var parts = refreshToken.Split('.');
            if (parts.Length != 4
                || parts[0] != RefreshTokenFormat
                || !Guid.TryParseExact(parts[1], "N", out _)
                || !long.TryParse(
                    parts[2],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out generation)
                || generation < 1)
            {
                return false;
            }

            sessionId = parts[1];
            return true;
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
            VerificationPurpose purpose,
            bool replaceExisting = false
        )
        {
            try
            {
                var existingState = await GetVerificationStateAsync(user.Email, purpose);

                // Both forms are compared, not just the key. Username is already normalised, so a
                // second signup that only corrects the casing — thomast to ThomasT, before the
                // first challenge was verified — matches on the key alone and would reuse the
                // cached state, creating the account with the casing the user just changed away
                // from. Comparing the display too rotates the state instead.
                var usernameMatches = purpose != VerificationPurpose.SignUp
                    || (string.Equals(
                            existingState?.Username,
                            user.Username,
                            StringComparison.Ordinal
                        )
                        && string.Equals(
                            existingState?.UsernameDisplay,
                            user.UsernameDisplay,
                            StringComparison.Ordinal
                        ));

                if (existingState != null && !replaceExisting && usernameMatches)
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

                if (existingState != null)
                {
                    _ = await _cacheService.DeleteKeyAsync(
                        VerificationTokenKey(existingState.LinkToken)
                    );
                    await DeleteVerificationStateAsync(existingState);
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
                    payload.Username,
                    expiresAtUtc,
                    challenge,
                    otpCode,
                    payload.UserId
                );

                var linkStored = await _cacheService.SetValueAsync(
                    key: VerificationTokenKey(linkToken),
                    value: JsonConvert.SerializeObject(payload),
                    expiry: VERIFY_TTL
                );

                var state = new VerificationDeliveryState
                {
                    Email = user.Email,
                    UserId = payload.UserId,
                    AuthVersion = payload.AuthVersion,
                    Purpose = purpose,
                    LinkToken = linkToken,
                    OtpCode = otpCode,
                    OtpChallenge = challenge,
                    OtpProof = otpProof,
                    Password = payload.Password,
                    Usertype = payload.Usertype,
                    Username = payload.Username,
                    UsernameDisplay = payload.UsernameDisplay,
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

        public async Task<User> VerifyVerificationToken(
            string token,
            VerificationPurpose expectedPurpose,
            Func<User, Task>? beforeConsume = null)
        {
            try
            {
                string? json = await _cacheService.GetValueAsync(VerificationTokenKey(token));

                if (string.IsNullOrEmpty(json))
                    throw VerificationRejected(
                        expectedPurpose,
                        AuthSecurityReason.Expired,
                        "Invalid or expired verification token.");

                var payload = JsonConvert.DeserializeObject<VerificationTokenPayload>(json)
                    ?? throw VerificationRejected(
                        expectedPurpose,
                        AuthSecurityReason.InvalidState,
                        "Invalid verification token payload.");

                if (payload.Purpose != expectedPurpose)
                    throw VerificationRejected(
                        expectedPurpose,
                        AuthSecurityReason.InvalidState,
                        "Verification token purpose mismatch.",
                        payload.UserId);

                if (beforeConsume != null)
                    await beforeConsume(CreateUserFromPayload(payload));

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
            VerificationPurpose expectedPurpose,
            Func<User, Task>? beforeConsume = null
        )
        {
            try
            {
                var state = await GetVerificationStateByChallengeAsync(challenge);
                if (state == null)
                    throw VerificationRejected(
                        expectedPurpose,
                        AuthSecurityReason.Expired,
                        "Invalid or expired verification challenge.");

                if (state.Purpose != expectedPurpose)
                    throw VerificationRejected(
                        expectedPurpose,
                        AuthSecurityReason.InvalidState,
                        "Verification challenge purpose mismatch.",
                        state.UserId);

                var expectedProof = ComputeOtpProof(
                    state.Purpose,
                    state.Email,
                    state.Password,
                    state.Usertype,
                    state.Username,
                    state.ExpiresAtUtc,
                    challenge,
                    code,
                    state.UserId
                );

                if (state.OtpChallenge != challenge)
                    throw VerificationRejected(
                        expectedPurpose,
                        AuthSecurityReason.Expired,
                        "Invalid or expired verification challenge.",
                        state.UserId,
                        state.Email);

                if (!CryptoHelper.FixedTimeEquals(state.OtpProof, expectedProof))
                {
                    var attempts = await RecordFailedOtpAttemptAsync(challenge);
                    if (attempts >= MAX_OTP_ATTEMPTS)
                    {
                        _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                        await DeleteVerificationStateAsync(state);
                    }

                    throw VerificationRejected(
                        expectedPurpose,
                        attempts >= MAX_OTP_ATTEMPTS
                            ? AuthSecurityReason.AttemptsExhausted
                            : AuthSecurityReason.InvalidCode,
                        "Invalid or expired verification code.",
                        state.UserId,
                        state.Email);
                }

                var verifiedUser = CreateUserFromPayload(new VerificationTokenPayload
                {
                    Email = state.Email,
                    UserId = state.UserId,
                    AuthVersion = state.AuthVersion,
                    Password = state.Password,
                    Usertype = state.Usertype,
                    Username = state.Username,
                    UsernameDisplay = state.UsernameDisplay,
                    Purpose = state.Purpose,
                });

                if (beforeConsume != null)
                    await beforeConsume(verifiedUser);

                _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                await DeleteVerificationStateAsync(state);
                _ = await _cacheService.DeleteKeyAsync(OtpAttemptKey(challenge));

                return verifiedUser;
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

        /// <summary>
        /// Issues the link token and OTP for an email change. Unlike signup and reset, the state
        /// is keyed by an address the account does not own yet, so the artifacts are also indexed
        /// by account id - see <c>EmailChangeIndexKey</c>.
        /// </summary>
        public async Task<VerificationArtifacts> GenerateEmailChangeArtifactsAsync(
            int userId,
            int authVersion,
            string newEmail
        )
        {
            // Cancel, generate and index have to happen as one unit, and consumption has to take
            // the same lock. Each pending change is stored under its own target address, so
            // without this two concurrent requests both clear the same (empty) index and both mint
            // a redeemable token, and a consumption racing a generation deletes the index the
            // generation just wrote - in both cases leaving a live proof that neither the pending
            // endpoint nor a later cancel can reach.
            return await WithEmailChangeLockAsync(userId, async () =>
            {
                await CancelPendingEmailChangeCoreAsync(userId);

                var artifacts = await GenerateVerificationArtifactsAsync(
                    new User
                    {
                        Id = userId,
                        AuthVersion = authVersion,
                        Email = newEmail,
                        Usertype = PlaceholderUsertype,
                    },
                    VerificationPurpose.ChangeEmail,
                    replaceExisting: true
                );

                var indexed = await _cacheService.SetValueAsync(
                    key: EmailChangeIndexKey(userId),
                    value: newEmail,
                    expiry: VERIFY_TTL
                );

                if (!indexed)
                    throw new NotAvailableException();

                return artifacts;
            });
        }

        public async Task<PendingEmailChange> ConsumeEmailChangeTokenAsync(string token)
        {
            try
            {
                // Read the payload first, only to learn whose lock to take.
                string? peek = await _cacheService.GetValueAsync(VerificationTokenKey(token));
                if (string.IsNullOrEmpty(peek))
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.Expired, "Invalid or expired email change link.");

                var peeked = JsonConvert.DeserializeObject<VerificationTokenPayload>(peek)
                    ?? throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Invalid verification token payload.");

                // Checked before the account id so a token for another purpose is reported as
                // such, rather than as one missing a binding it was never meant to carry.
                if (peeked.Purpose != VerificationPurpose.ChangeEmail)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Verification token purpose mismatch.");

                if (peeked.UserId is not int owner)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Email change token is missing its account.");

                return await WithEmailChangeLockAsync(owner, () => ConsumeTokenCoreAsync(token));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] ConsumeEmailChangeTokenAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task<PendingEmailChange> ConsumeTokenCoreAsync(string token)
        {
            try
            {
                string? json = await _cacheService.GetValueAsync(VerificationTokenKey(token));

                if (string.IsNullOrEmpty(json))
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.Expired, "Invalid or expired email change link.");

                var payload = JsonConvert.DeserializeObject<VerificationTokenPayload>(json)
                    ?? throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Invalid verification token payload.");

                if (payload.Purpose != VerificationPurpose.ChangeEmail)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Verification token purpose mismatch.");

                if (payload.UserId is not int userId)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Email change token is missing its account.");

                if (payload.AuthVersion is not int authVersion)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Email change token is missing its account.");

                var state = await GetVerificationStateAsync(payload.Email, payload.Purpose);
                var expiresAtUtc = state?.ExpiresAtUtc ?? DateTime.UtcNow;

                _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(token));
                if (state != null)
                    await DeleteVerificationStateAsync(state);
                else
                    _ = await _cacheService.DeleteKeyAsync(
                        VerificationStateKey(payload.Email, payload.Purpose)
                    );

                _ = await _cacheService.DeleteKeyAsync(EmailChangeIndexKey(userId));

                return new PendingEmailChange(userId, authVersion, payload.Email, expiresAtUtc);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] ConsumeTokenCoreAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<PendingEmailChange> ConsumeEmailChangeOtpAsync(
            string code,
            string challenge
        )
        {
            try
            {
                var peek = await GetVerificationStateByChallengeAsync(challenge)
                    ?? throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.Expired, "Invalid or expired verification challenge.");

                if (peek.Purpose != VerificationPurpose.ChangeEmail)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Verification challenge purpose mismatch.");

                if (peek.UserId is not int owner)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Email change challenge is missing its account.");

                return await WithEmailChangeLockAsync(
                    owner,
                    () => ConsumeOtpCoreAsync(code, challenge));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] ConsumeEmailChangeOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task<PendingEmailChange> ConsumeOtpCoreAsync(string code, string challenge)
        {
            try
            {
                var state = await GetVerificationStateByChallengeAsync(challenge);
                if (state == null)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.Expired, "Invalid or expired verification challenge.");

                if (state.Purpose != VerificationPurpose.ChangeEmail)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Verification challenge purpose mismatch.");

                if (state.UserId is not int userId)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Email change challenge is missing its account.");

                if (state.AuthVersion is not int authVersion)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.InvalidState, "Email change challenge is missing its account.");

                if (state.OtpChallenge != challenge)
                    throw VerificationRejected(VerificationPurpose.ChangeEmail, AuthSecurityReason.Expired, "Invalid or expired verification challenge.");

                var expectedProof = ComputeOtpProof(
                    state.Purpose,
                    state.Email,
                    state.Password,
                    state.Usertype,
                    state.Username,
                    state.ExpiresAtUtc,
                    challenge,
                    code,
                    state.UserId
                );

                if (!CryptoHelper.FixedTimeEquals(state.OtpProof, expectedProof))
                {
                    var attempts = await RecordFailedOtpAttemptAsync(challenge);
                    if (attempts >= MAX_OTP_ATTEMPTS)
                    {
                        _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                        await DeleteVerificationStateAsync(state);
                        _ = await _cacheService.DeleteKeyAsync(EmailChangeIndexKey(userId));
                    }

                    throw VerificationRejected(
                        VerificationPurpose.ChangeEmail,
                        attempts >= MAX_OTP_ATTEMPTS
                            ? AuthSecurityReason.AttemptsExhausted
                            : AuthSecurityReason.InvalidCode,
                        "Invalid or expired verification code.",
                        userId);
                }

                _ = await _cacheService.DeleteKeyAsync(VerificationTokenKey(state.LinkToken));
                await DeleteVerificationStateAsync(state);
                _ = await _cacheService.DeleteKeyAsync(OtpAttemptKey(challenge));
                _ = await _cacheService.DeleteKeyAsync(EmailChangeIndexKey(userId));

                return new PendingEmailChange(userId, authVersion, state.Email, state.ExpiresAtUtc);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] ConsumeOtpCoreAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<PendingEmailChange?> GetPendingEmailChangeAsync(int userId)
        {
            try
            {
                var email = await _cacheService.GetValueAsync(EmailChangeIndexKey(userId));
                if (string.IsNullOrWhiteSpace(email))
                    return null;

                var state = await GetVerificationStateAsync(email, VerificationPurpose.ChangeEmail);

                // The index can outlive the state it points at: the state is deleted on redemption
                // and after too many failed OTP attempts, and both keys expire independently.
                if (state == null || state.UserId != userId || state.AuthVersion is not int version)
                {
                    _ = await _cacheService.DeleteKeyAsync(EmailChangeIndexKey(userId));
                    return null;
                }

                return new PendingEmailChange(userId, version, state.Email, state.ExpiresAtUtc);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] GetPendingEmailChangeAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public Task CancelPendingEmailChangeAsync(int userId) =>
            WithEmailChangeLockAsync<object?>(userId, async () =>
            {
                await CancelPendingEmailChangeCoreAsync(userId);
                return null;
            });

        private async Task CancelPendingEmailChangeCoreAsync(int userId)
        {
            try
            {
                var email = await _cacheService.GetValueAsync(EmailChangeIndexKey(userId));

                if (!string.IsNullOrWhiteSpace(email))
                {
                    var state = await GetVerificationStateAsync(
                        email,
                        VerificationPurpose.ChangeEmail
                    );

                    if (state != null && state.UserId == userId)
                    {
                        _ = await _cacheService.DeleteKeyAsync(
                            VerificationTokenKey(state.LinkToken)
                        );
                        await DeleteVerificationStateAsync(state);
                    }
                }

                _ = await _cacheService.DeleteKeyAsync(EmailChangeIndexKey(userId));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] CancelPendingEmailChangeCoreAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        // Session and proof go together, so revocation can never leave a proof behind a deleted
        // session (or report success when the cache dropped half way).
        private const string RevokeSessionAndProofScript = @"
redis.call('DEL', KEYS[1], KEYS[2])
return 1";

        public async Task<StepUpProofWriteResult> StoreStepUpProofAsync(
            string sessionId,
            int userId,
            long expectedGeneration,
            string proofJson,
            TimeSpan lifetime
        )
        {
            var result = await _cacheService.TryEvalAsync(
                StoreStepUpProofScript,
                [
                    (StackExchange.Redis.RedisKey)SessionKey(sessionId),
                    (StackExchange.Redis.RedisKey)StepUpProofKeys.ForSession(sessionId),
                    (StackExchange.Redis.RedisKey)StepUpProofKeys.GenerationForUser(userId),
                ],
                [proofJson, checked((long)lifetime.TotalMilliseconds), expectedGeneration]
            );

            if (!result.Succeeded || result.Value is null)
                return StepUpProofWriteResult.Unavailable;

            return ToLong(result.Value) switch
            {
                1 => StepUpProofWriteResult.Stored,
                2 => StepUpProofWriteResult.Superseded,
                _ => StepUpProofWriteResult.SessionEnded,
            };
        }

        /// <summary>
        /// Writes the session's use to the registry once Redis holds it. The registry, not Redis,
        /// decides whether the session is alive, so a row revoked in the meantime refuses the
        /// refresh here, and a new session that could not be recorded is not handed out.
        /// </summary>
        private async Task RecordSessionUseAsync(
            RefreshSessionState session,
            ClientRequestInfo requestInfo,
            TimeSpan refreshTtl,
            bool isNew)
        {
            if (_sessions is null)
                return;

            // Read after the Redis write, so the recorded expiry is never earlier than the key's.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var record = new AuthSession
            {
                Id = session.SessionId,
                UserId = session.UserId,
                Transport = session.Transport,
                RememberMe = session.RememberMe,
                CreatedAt = isNew ? now : session.CreatedAt,
                LastSeenAt = now,
                ExpiresAt = now + refreshTtl,
            };
            record.DescribeClient(requestInfo);

            bool recorded;
            try
            {
                recorded = await _sessions.RecordUseAsync(record);
            }
            catch when (isNew)
            {
                // Nothing has been handed out yet: take the new session back out of Redis so it
                // cannot be used without a row. An existing session is left for its next refresh.
                await TryRevokeRefreshSessionCoreAsync(session.SessionId);
                throw;
            }

            if (!recorded)
            {
                await TryRevokeRefreshSessionCoreAsync(session.SessionId);
                throw new UnauthorizedException("Refresh session is invalid or expired.");
            }
        }

        public async Task RevokeRefreshSessionAsync(string sessionId, AuthSecurityReason reason)
        {
            // The registry first: once its row is revoked neither kind of token for the session is
            // accepted, whatever then happens to the Redis cleanup.
            var recordedUserId = _sessions is null
                ? null
                : await _sessions.RevokeAsync(sessionId, reason, _timeProvider.GetUtcNow().UtcDateTime);

            var userId = await CleanUpRefreshSessionAsync(sessionId, revokedInRegistry: recordedUserId is not null)
                ?? recordedUserId;
            if (userId is not null)
                EmitSessionRevoked(userId.Value, sessionId, reason);
        }

        public async Task<bool> RevokeUserRefreshSessionAsync(
            int userId,
            string sessionId,
            AuthSecurityReason reason)
        {
            var sessions = _sessions
                ?? throw new InvalidOperationException("Revoking a session by owner needs the session registry.");

            if (!await sessions.RevokeForUserAsync(userId, sessionId, reason, _timeProvider.GetUtcNow().UtcDateTime))
                return false;

            await CleanUpRefreshSessionAsync(sessionId, revokedInRegistry: true);
            EmitSessionRevoked(userId, sessionId, reason);
            return true;
        }

        public async Task<int> RevokeOtherRefreshSessionsAsync(
            int userId,
            string currentSessionId,
            AuthSecurityReason reason)
        {
            var sessions = _sessions
                ?? throw new InvalidOperationException("Revoking other sessions needs the session registry.");

            var revoked = await sessions.RevokeAllAsync(
                userId,
                reason,
                _timeProvider.GetUtcNow().UtcDateTime,
                exceptSessionId: currentSessionId);
            var cleaned = await SweepRefreshSessionsAsync(userId, revoked, exceptSessionId: currentSessionId);

            // A session from before the registry has no row, so only the Redis sweep ends it. When
            // the cache cannot list them the caller is told, and can retry, rather than shown a
            // success that may have left one signed in.
            var total = revoked.Union(cleaned ?? []).Count();
            if (total > 0)
                EmitSessionsRevoked(userId, reason, actorUserId: null, AuthSecurityOutcome.Succeeded);
            if (cleaned is null)
                throw new NotAvailableException();
            return total;
        }

        private void EmitSessionRevoked(int userId, string sessionId, AuthSecurityReason reason) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.SessionRevoked,
                Outcome = AuthSecurityOutcome.Succeeded,
                Reason = reason,
                UserId = userId,
                SessionId = sessionId,
            });

        /// <summary>
        /// Removes the session from Redis. When the registry already revoked it, an unreachable
        /// cache is logged rather than failing the caller: the row refuses both kinds of token, and
        /// the key ages out with its TTL.
        /// </summary>
        private async Task<int?> CleanUpRefreshSessionAsync(string sessionId, bool revokedInRegistry)
        {
            try
            {
                return await RevokeRefreshSessionCoreAsync(sessionId);
            }
            catch (NotAvailableException) when (revokedInRegistry)
            {
                Logger.Warn(
                    $"[TokenService] Session {sessionId} is revoked in the registry; its Redis cleanup is deferred to the key's expiry.");
                return null;
            }
        }

        private async Task TryRevokeRefreshSessionCoreAsync(string sessionId)
        {
            try
            {
                await RevokeRefreshSessionCoreAsync(sessionId);
            }
            catch (Exception e)
            {
                Logger.Warn(e, $"[TokenService] Could not withdraw refresh session {sessionId} from Redis.");
            }
        }

        /// <returns>The session's account, or null when no such session was stored.</returns>
        private async Task<int?> RevokeRefreshSessionCoreAsync(string sessionId)
        {
            try
            {
                var session = await GetRefreshSessionAsync(sessionId);
                if (session != null)
                {
                    // Only a session from before derived tokens has a stored token to remove;
                    // a derived one dies with the session's secret.
                    if (!string.IsNullOrWhiteSpace(session.CurrentRefreshTokenHash))
                        await _cacheService.DeleteKeyAsync(TokenKey(session.CurrentRefreshTokenHash));

                    await _cacheService.SetRemoveAsync(UserSessionsKey(session.UserId), session.SessionId);
                }

                // One atomic delete of the session and its proof. A concurrent step-up verify either
                // wrote before this (and is removed with it) or now sees no session and writes nothing.
                // An unreachable cache fails the revocation instead of reporting a logout that left
                // the proof usable.
                var revoked = await _cacheService.TryEvalAsync(
                    RevokeSessionAndProofScript,
                    [
                        (StackExchange.Redis.RedisKey)SessionKey(sessionId),
                        (StackExchange.Redis.RedisKey)StepUpProofKeys.ForSession(sessionId),
                    ],
                    []
                );
                if (!revoked.Succeeded)
                    throw new NotAvailableException();

                return session?.UserId;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] RevokeRefreshSessionAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RevokeAllRefreshSessionsAsync(
            int userId,
            AuthSecurityReason reason,
            int? actorUserId = null)
        {
            try
            {
                // The registry first, which ends every recorded session at once, access tokens
                // included. Callers that rotate credentials also bump AuthVersion first, which
                // covers a session from before the registry that the Redis sweep below misses.
                IReadOnlyList<string> recorded = _sessions is null
                    ? []
                    : await _sessions.RevokeAllAsync(userId, reason, _timeProvider.GetUtcNow().UtcDateTime);

                IReadOnlyList<string>? cleaned = await SweepRefreshSessionsAsync(userId, recorded);
                if (cleaned is null)
                {
                    EmitSessionsRevoked(
                        userId,
                        reason,
                        actorUserId,
                        recorded.Count > 0 ? AuthSecurityOutcome.Succeeded : AuthSecurityOutcome.Faulted);
                    return;
                }

                // One event for the sweep, with no session id: it covers every session. Nothing is
                // recorded when there was nothing to end, such as a disabled account refreshing again.
                if (recorded.Union(cleaned).Any())
                    EmitSessionsRevoked(userId, reason, actorUserId, AuthSecurityOutcome.Succeeded);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[TokenService] RevokeAllRefreshSessionsAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        /// <summary>
        /// Removes the account's sessions from Redis, all of them or all but
        /// <paramref name="exceptSessionId"/>.
        /// </summary>
        /// <param name="revokedInRegistry">
        /// Sessions whose rows are already revoked, for which an unreachable cache is not a failure.
        /// </param>
        /// <returns>
        /// The sessions found and removed, or null when the cache could not list them.
        /// </returns>
        private async Task<IReadOnlyList<string>?> SweepRefreshSessionsAsync(
            int userId,
            IReadOnlyList<string> revokedInRegistry,
            string? exceptSessionId = null)
        {
            // Read through a script so that an unreachable cache is told apart from an account
            // with no sessions; the plain set read reports both as empty.
            var read = await _cacheService.TryEvalAsync(
                ReadSessionIdsScript,
                [(StackExchange.Redis.RedisKey)UserSessionsKey(userId)],
                []);
            if (!read.Succeeded)
                return null;

            var cleaned = new List<string>();
            foreach (var sessionId in ReadMembers(read.Value))
            {
                if (sessionId == exceptSessionId)
                    continue;

                if (await CleanUpRefreshSessionAsync(sessionId, revokedInRegistry.Contains(sessionId)) is not null)
                    cleaned.Add(sessionId);
            }

            // Kept while a session is meant to survive; each removed one left the set above.
            if (exceptSessionId is null)
                await _cacheService.DeleteKeyAsync(UserSessionsKey(userId));

            return cleaned;
        }

        private void EmitSessionsRevoked(
            int userId,
            AuthSecurityReason reason,
            int? actorUserId,
            AuthSecurityOutcome outcome) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.SessionRevoked,
                Outcome = outcome,
                Reason = reason,
                UserId = userId,
                ActorUserId = actorUserId,
            });

        private static string[] ReadMembers(object? value) => value switch
        {
            StackExchange.Redis.RedisResult result when !result.IsNull =>
                ((StackExchange.Redis.RedisResult[]?)result ?? []).Select(member => member.ToString()).ToArray(),
            object?[] items => items.Where(item => item is not null).Select(item => item!.ToString()!).ToArray(),
            _ => [],
        };

        private async Task<RefreshSessionState?> GetRefreshSessionAsync(string sessionId) =>
            (await ReadRefreshSessionAsync(sessionId)).Session;

        /// <returns>The session and the exact stored value, for a compare-and-set write back.</returns>
        private async Task<(RefreshSessionState? Session, string? Stored)> ReadRefreshSessionAsync(string sessionId)
        {
            var json = await _cacheService.GetValueAsync(SessionKey(sessionId));
            if (string.IsNullOrWhiteSpace(json))
                return (null, null);

            return (JsonConvert.DeserializeObject<RefreshSessionState>(json), json);
        }

        private VerificationTokenPayload BuildVerificationPayload(
            User user,
            VerificationPurpose purpose
        )
        {
            return new VerificationTokenPayload
            {
                Email = user.Email,
                // Signup builds an in-memory user that has never been inserted, so its Id is 0.
                // Only an email change supplies a real account id here.
                UserId = user.Id > 0 ? user.Id : null,
                AuthVersion = user.Id > 0 ? user.AuthVersion : null,
                Password = purpose == VerificationPurpose.SignUp ? user.Password : null,
                Username = purpose == VerificationPurpose.SignUp ? user.Username : null,
                UsernameDisplay = purpose == VerificationPurpose.SignUp ? user.UsernameDisplay : null,
                Usertype = purpose == VerificationPurpose.SignUp
                    ? AuthRoles.NormalizeStored(user.Usertype)
                    : PlaceholderUsertype,
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
                Username = payload.Username,
                UsernameDisplay = payload.UsernameDisplay,
                Usertype = AuthRoles.NormalizeStored(payload.Usertype),
            };
        }

        private static string ComputeTokenHash(string token) => CryptoHelper.HashToken(token);

        private string ComputeOtpProof(
            VerificationPurpose purpose,
            string email,
            string? password,
            string? usertype,
            string? username,
            DateTime expiresAtUtc,
            string challenge,
            string otpCode,
            int? userId = null
        )
        {
            var fields = new List<object?>
            {
                purpose,
                email,
                password ?? string.Empty,
                usertype ?? string.Empty,
            };
            if (username != null)
                fields.Add(username);
            fields.Add(expiresAtUtc.ToUniversalTime().Ticks);
            fields.Add(challenge);
            fields.Add(otpCode);
            // Appended last and only when present, so proofs already issued for the signup and
            // reset purposes hash to the same value across the deploy that adds this.
            if (userId.HasValue)
                fields.Add(userId.Value);

            var material = string.Join("|", fields);

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

        // Reverse index from account to pending target address. The verification state itself is
        // keyed by the NEW email, which nothing knows until the change is confirmed, so without
        // this a user could neither be shown their pending change nor cancel it — and a second
        // request to a different address would strand the first one until its TTL ran out.
        private static string EmailChangeIndexKey(int userId) =>
            $"verify:user-email-change:{userId}";

        private async Task<T> WithEmailChangeLockAsync<T>(int userId, Func<Task<T>> action)
        {
            var lockValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var lockKey = EmailChangeLockKey(userId);
            if (!await _cacheService.AcquireLockAsync(lockKey, lockValue, EmailChangeLockTtl))
                throw new ConflictException(
                    "Another email change request is already being processed."
                );

            try
            {
                return await action();
            }
            finally
            {
                await _cacheService.ReleaseLockAsync(lockKey, lockValue);
            }
        }

        private static string EmailChangeLockKey(int userId) =>
            $"verify:user-email-change-lock:{userId}";

        /// <summary>A stored random refresh token, the format issued before derived tokens.</summary>
        private sealed class RefreshTokenRecord
        {
            public required string SessionId
            {
                get; set;
            }
            public int UserId
            {
                get; set;
            }
            public DateTime CreatedAt
            {
                get; set;
            }
            /// <summary>
            /// When the token was rotated away. Null while it is the session's live token,
            /// including every record stored before retired tokens were kept.
            /// </summary>
            public DateTime? RetiredAt
            {
                get; set;
            }
        }

        private sealed class RefreshSessionState
        {
            public required string SessionId
            {
                get; set;
            }
            public int UserId
            {
                get; set;
            }
            /// <summary>
            /// The account's AuthVersion when the session was issued or last rotated. Null for a
            /// session stored before this was recorded; it is stamped on its next refresh.
            /// </summary>
            public int? AuthVersion
            {
                get; set;
            }
            public SessionTransport Transport
            {
                get; set;
            }
            /// <summary>
            /// Base64 key that derives every refresh token of the session. Null for a session
            /// stored before derived tokens; it is created on that session's next refresh.
            /// </summary>
            public string? TokenSecret
            {
                get; set;
            }
            /// <summary>The generation of the one live refresh token; earlier ones are retired.</summary>
            public long Generation
            {
                get; set;
            }
            /// <summary>When the generation last advanced, the start of its grace window.</summary>
            public DateTime? RotatedAt
            {
                get; set;
            }
            /// <summary>
            /// Hash of the stored random token, for a session from before derived tokens only.
            /// Empty once the session has moved onto them.
            /// </summary>
            public required string CurrentRefreshTokenHash
            {
                get; set;
            }
            public required string CurrentBindingTokenHash
            {
                get; set;
            }
            public bool RememberMe
            {
                get; set;
            }
            public string LastSeenIpAddress { get; set; } = "Unknown";
            public string LastSeenClientName { get; set; } = "Unknown";
            public string LastSeenDeviceType { get; set; } = "Unknown";
            public DateTime CreatedAt
            {
                get; set;
            }
            public DateTime LastSeenAt
            {
                get; set;
            }
        }

        private sealed class VerificationTokenPayload
        {
            public required string Email
            {
                get; set;
            }
            /// <summary>
            /// Set only for <see cref="VerificationPurpose.ChangeEmail"/>, where the account
            /// already exists. Null for signup and reset, whose payloads describe a user that is
            /// either not yet persisted or identified by email alone.
            /// </summary>
            public int? UserId
            {
                get; set;
            }
            public int? AuthVersion
            {
                get; set;
            }
            public string? Password
            {
                get; set;
            }
            public string? Username
            {
                get; set;
            }

            /// <summary>
            /// The casing the user typed at signup, carried so email verification can store it.
            /// </summary>
            /// <remarks>
            /// Deliberately absent from <c>ComputeOtpProof</c>. Adding it would change the HMAC
            /// input and invalidate every challenge minted before this deployed; it is a
            /// presentation string with no bearing on what the token authorises, and a payload
            /// written by the previous version simply deserialises it as null, which
            /// <c>AuthUserRepository.CreateUserAsync</c> repairs from the username.
            /// </remarks>
            public string? UsernameDisplay
            {
                get; set;
            }
            public required string Usertype
            {
                get; set;
            }
            public VerificationPurpose Purpose
            {
                get; set;
            }
        }

        private sealed class VerificationDeliveryState
        {
            public required string Email
            {
                get; set;
            }
            public int? UserId
            {
                get; set;
            }
            public int? AuthVersion
            {
                get; set;
            }
            public VerificationPurpose Purpose
            {
                get; set;
            }
            public required string LinkToken
            {
                get; set;
            }
            public required string OtpCode
            {
                get; set;
            }
            public required string OtpChallenge
            {
                get; set;
            }
            public required string OtpProof
            {
                get; set;
            }
            public string? Password
            {
                get; set;
            }
            public string? Username
            {
                get; set;
            }

            /// <summary>
            /// The casing the user typed at signup, carried so email verification can store it.
            /// </summary>
            /// <remarks>
            /// Deliberately absent from <c>ComputeOtpProof</c>. Adding it would change the HMAC
            /// input and invalidate every challenge minted before this deployed; it is a
            /// presentation string with no bearing on what the token authorises, and a payload
            /// written by the previous version simply deserialises it as null, which
            /// <c>AuthUserRepository.CreateUserAsync</c> repairs from the username.
            /// </remarks>
            public string? UsernameDisplay
            {
                get; set;
            }
            public required string Usertype
            {
                get; set;
            }
            public DateTime ExpiresAtUtc
            {
                get; set;
            }
        }
    }
}

