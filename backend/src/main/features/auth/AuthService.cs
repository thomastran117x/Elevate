using backend.main.dtos;
using backend.main.application.security;
using backend.main.dtos.general;
using backend.main.features.auth.contracts;
using backend.main.features.auth.device;
using backend.main.features.auth.oauth;
using backend.main.features.auth.repositories;
using backend.main.features.auth.token;
using backend.main.shared.exceptions.http;
using backend.main.models.core;
using backend.main.services.interfaces;
using backend.main.features.cache;

using Newtonsoft.Json;
using System.Security.Cryptography;
using backend.main.shared.providers;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth
{
    public class AuthService : IAuthService
    {
        private readonly IAuthUserRepository _userRepository;
        private readonly IOAuthService _oauthService;
        private readonly ITokenService _tokenService;
        private readonly ICacheService _cacheService;
        private readonly IPublisher _publisher;
        private readonly IDeviceService _deviceService;
        private readonly ClientRequestInfo _requestInfo;
        private const string DummyHash = "$2a$11$9FJqO6j/4jP3E2fOQdWgMuKZXWWvPZ09f8Pj0L9VqB6TfqZ4fE5SO";
        private static readonly TimeSpan PendingOAuthSignupTtl = TimeSpan.FromMinutes(15);

        public AuthService(
            IAuthUserRepository userRepository,
            IOAuthService oauthService,
            ITokenService tokenService,
            ICacheService cacheService,
            IPublisher publisher,
            IDeviceService deviceService,
            ClientRequestInfo requestInfo
        )
        {
            _userRepository = userRepository;
            _oauthService = oauthService;
            _tokenService = tokenService;
            _cacheService = cacheService;
            _publisher = publisher;
            _deviceService = deviceService;
            _requestInfo = requestInfo;
        }

        public async Task<UserToken> LoginAsync(
            string email,
            string password,
            SessionTransport transport,
            bool rememberMe = false
        )
        {
            try
            {
                UserAuthRecord? user = await _userRepository.GetAuthByEmailAsync(email);

                var hashToCheck = user?.Password ?? DummyHash;

                bool isValidPassword = VerifyPassword(password, hashToCheck);

                if (user == null || user.Password == null || !isValidPassword)
                    throw new UnauthorizedException("Invalid email or password");

                await EnsureUserEnabledAsync(ToUser(user));
                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return await GenerateTokenPair(ToUser(user), transport, rememberMe: rememberMe);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] LoginAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<VerificationOtpChallenge> SignUpAsync(string email, string password, string userType)
        {
            try
            {
                if (await _userRepository.EmailExistsAsync(email))
                    throw new ConflictException($"An account is already registered with the email: {email}");

                userType = AuthRoles.NormalizeOrThrow(userType);
                string hashedPassword = HashPassword(password);

                User user = new User
                {
                    Email = email,
                    Password = hashedPassword,
                    Usertype = userType
                };

                var artifacts = await _tokenService.GenerateVerificationArtifactsAsync(
                    user,
                    VerificationPurpose.SignUp
                );
                var message = new EmailMessage
                {
                    Type = EmailMessageType.VerifyEmail,
                    Email = email,
                    Token = artifacts.LinkToken,
                    Code = artifacts.OtpChallenge.Code
                };

                await _publisher.PublishAsync("eventxperience-email", message);

                return artifacts.OtpChallenge;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[InternalServerErrorException] SignUpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyAsync(string token, SessionTransport transport)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationToken(
                    token,
                    VerificationPurpose.SignUp
                );

                if (await _userRepository.EmailExistsAsync(user.Email))
                    throw new ConflictException($"An account is already registered with the email: {user.Email}");

                await _userRepository.CreateUserAsync(user);

                return await GenerateTokenPair(user, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyOtpAsync(
            string code,
            string challenge,
            SessionTransport transport
        )
        {
            try
            {
                var user = await _tokenService.VerifyVerificationOtpAsync(
                    code,
                    challenge,
                    VerificationPurpose.SignUp
                );

                if (await _userRepository.EmailExistsAsync(user.Email))
                    throw new ConflictException($"An account is already registered with the email: {user.Email}");

                await _userRepository.CreateUserAsync(user);

                return await GenerateTokenPair(user, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<VerificationOtpChallenge> ForgotPasswordAsync(string email)
        {
            try
            {
                var existingUser = await _userRepository.GetAuthByEmailAsync(email);
                if (existingUser == null || existingUser.IsDisabled)
                    return BuildPlaceholderForgotPasswordChallenge();

                User user = new User
                {
                    Email = email,
                    Password = "placeholder",
                    Usertype = "placeholder"
                };

                var artifacts = await _tokenService.GenerateVerificationArtifactsAsync(
                    user,
                    VerificationPurpose.ResetPassword
                );
                var message = new EmailMessage
                {
                    Type = EmailMessageType.ResetPassword,
                    Email = email,
                    Token = artifacts.LinkToken,
                    Code = artifacts.OtpChallenge.Code
                };

                await _publisher.PublishAsync("eventxperience-email", message);

                return artifacts.OtpChallenge;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] ForgotPasswordAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task ChangePasswordAsync(string token, string password)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationToken(
                    token,
                    VerificationPurpose.ResetPassword
                );
                await ChangePasswordInternalAsync(user.Email, password);
                return;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] ChangePasswordAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task ChangePasswordWithOtpAsync(string code, string challenge, string password)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationOtpAsync(
                    code,
                    challenge,
                    VerificationPurpose.ResetPassword
                );
                await ChangePasswordInternalAsync(user.Email, password);
                return;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] ChangePasswordWithOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<OAuthAuthenticationResult> GoogleAsync(
            string token,
            SessionTransport transport,
            string? expectedNonce = null
        )
        {
            try
            {
                OAuthUser oauthUser = await _oauthService.VerifyGoogleTokenAsync(
                    token,
                    expectedNonce
                );
                if (oauthUser == null)
                    throw new UnauthorizedException("Invalid Google Token");

                var user = await ResolveGoogleUserAsync(oauthUser);

                if (user == null)
                    return OAuthAuthenticationResult.RoleSelectionRequired(
                        await CreatePendingOAuthSignupAsync(oauthUser, transport)
                    );

                await EnsureUserEnabledAsync(user);
                user = await EnsureOAuthRoleAsync(user);
                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return OAuthAuthenticationResult.Authenticated(
                    await GenerateTokenPair(user, transport)
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GoogleAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<OAuthAuthenticationResult> GoogleCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri,
            SessionTransport transport,
            string? nonce = null
        )
        {
            try
            {
                var idToken = await _oauthService.ExchangeGoogleCodeAsync(code, codeVerifier, redirectUri);
                return await GoogleAsync(idToken, transport, nonce);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GoogleCodeAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<OAuthAuthenticationResult> MicrosoftAsync(
            string token,
            SessionTransport transport,
            string? expectedNonce = null
        )
        {
            try
            {
                OAuthUser oauthUser = await _oauthService.VerifyMicrosoftTokenAsync(
                    token,
                    expectedNonce
                );
                if (oauthUser == null)
                    throw new UnauthorizedException("Invalid Microsoft Token");

                var user = await ResolveMicrosoftUserAsync(oauthUser);

                if (user == null)
                    return OAuthAuthenticationResult.RoleSelectionRequired(
                        await CreatePendingOAuthSignupAsync(oauthUser, transport)
                    );

                await EnsureUserEnabledAsync(user);
                user = await EnsureOAuthRoleAsync(user);
                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return OAuthAuthenticationResult.Authenticated(
                    await GenerateTokenPair(user, transport)
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] MicrosoftAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> CompleteOAuthSignupAsync(
            string signupToken,
            string usertype,
            SessionTransport transport
        )
        {
            try
            {
                usertype = AuthRoles.NormalizeOrThrow(usertype);
                var pending = await GetPendingOAuthSignupAsync(signupToken)
                    ?? throw new UnauthorizedException(
                        "OAuth signup session is invalid or expired."
                    );

                if (pending.Transport != transport)
                    throw new UnauthorizedException("OAuth signup transport mismatch.");

                var oauthUser = new OAuthUser(
                    pending.ProviderUserId,
                    pending.Email,
                    pending.Name,
                    pending.Provider
                );
                var user = pending.Provider switch
                {
                    "google" => await ResolveGoogleUserAsync(oauthUser),
                    "microsoft" => await ResolveMicrosoftUserAsync(oauthUser),
                    _ => throw new BadRequestException("Unsupported OAuth provider."),
                };

                if (user == null)
                {
                    user = await _userRepository.CreateUserAsync(new User
                    {
                        Email = pending.Email,
                        Usertype = usertype,
                        GoogleID = pending.Provider == "google" ? pending.ProviderUserId : null,
                        MicrosoftID = pending.Provider == "microsoft"
                            ? pending.ProviderUserId
                            : null,
                    });
                }
                else
                {
                    await EnsureUserEnabledAsync(user);
                    user = await EnsureOAuthRoleAsync(user);
                }

                await _cacheService.DeleteKeyAsync(PendingOAuthSignupKey(signupToken));
                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);
                return await GenerateTokenPair(user, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] CompleteOAuthSignupAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<User> GetCurrentUserAsync(int userId)
        {
            try
            {
                var user = await _userRepository.GetUserAsync(userId)
                    ?? throw new ResourceNotFoundException($"User with ID {userId} is not found");

                await EnsureUserEnabledAsync(user);
                return user;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GetCurrentUserAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> HandleTokensAsync(
            string oldRefreshToken,
            string? sessionBindingToken,
            SessionTransport transport
        )
        {
            try
            {
                var validation = await _tokenService.ValidateRefreshToken(
                    oldRefreshToken,
                    sessionBindingToken,
                    transport,
                    _requestInfo
                );
                var user = await _userRepository.GetUserAsync(validation.UserId);
                if (user == null)
                    throw new ResourceNotFoundException($"User with ID {validation.UserId} is not found");
                await EnsureUserEnabledAsync(user, revokeSessions: true);

                return await GenerateTokenPair(
                    user,
                    validation.Transport,
                    validation.SessionId
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HandleTokensAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyDeviceLoginAsync(
            string token,
            SessionTransport transport
        )
        {
            try
            {
                return await _deviceService.VerifyDeviceAsync(token, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyDeviceLoginAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task HandleLogoutAsync(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport transport
        )
        {
            try
            {
                var validation = await _tokenService.ValidateRefreshToken(
                    refreshToken,
                    sessionBindingToken,
                    transport,
                    _requestInfo
                );
                await _tokenService.RevokeRefreshSessionAsync(validation.SessionId);
                return;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HashPassword failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private string HashPassword(string password)
        {
            try
            {
                return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HashPassword failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private bool VerifyPassword(string password, string hashedPassword)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyPassword failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task<UserToken> GenerateTokenPair(
            User user,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        )
        {
            try
            {
                user.Usertype = AuthRoles.NormalizeStored(user.Usertype);
                var accessToken = _tokenService.GenerateAccessToken(user);
                var refreshToken = await _tokenService.GenerateRefreshToken(
                    user.Id,
                    _requestInfo,
                    transport,
                    sessionId,
                    rememberMe
                );
                Token authToken = new Token(
                    accessToken.Value,
                    accessToken.ExpiresAtUtc,
                    refreshToken.Value,
                    refreshToken.SessionBindingToken,
                    refreshToken.Lifetime,
                    refreshToken.Transport
                );

                UserToken userToken = new(authToken, user);

                return userToken;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GenerateTokenPair failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private async Task ChangePasswordInternalAsync(string email, string password)
        {
            var hashedPassword = HashPassword(password);

            var existingUser = await _userRepository.GetAuthByEmailAsync(email)
                ?? throw new UnauthorizedException("Invalid token");
            await EnsureUserEnabledAsync(ToUser(existingUser));

            await _userRepository.UpdateUserAsync(existingUser.Id, new User
            {
                Email = existingUser.Email,
                Password = hashedPassword,
                Usertype = existingUser.Usertype,
            });
            await _userRepository.IncrementAuthVersionAsync(existingUser.Id);
            await _tokenService.RevokeAllRefreshSessionsAsync(existingUser.Id);
        }

        private async Task<User?> ResolveGoogleUserAsync(OAuthUser oauthUser)
        {
            var providerUser = await _userRepository.GetOAuthByGoogleIdAsync(oauthUser.Id);
            var emailUser = await _userRepository.GetOAuthByEmailAsync(oauthUser.Email);

            if (providerUser != null && emailUser != null && providerUser.Id != emailUser.Id)
                throw new ConflictException("This Google account is already linked to another user.");

            if (providerUser != null)
                return ToUser(providerUser);

            if (emailUser == null)
                return null;

            if (string.IsNullOrWhiteSpace(emailUser.GoogleID))
            {
                emailUser = await _userRepository.UpdateProviderIdsAsync(
                    emailUser.Id,
                    oauthUser.Id,
                    null
                ) ?? emailUser;
            }

            return ToUser(emailUser);
        }

        private async Task<User?> ResolveMicrosoftUserAsync(OAuthUser oauthUser)
        {
            var providerUser = await _userRepository.GetOAuthByMicrosoftIdAsync(oauthUser.Id);
            var emailUser = await _userRepository.GetOAuthByEmailAsync(oauthUser.Email);

            if (providerUser != null && emailUser != null && providerUser.Id != emailUser.Id)
                throw new ConflictException("This Microsoft account is already linked to another user.");

            if (providerUser != null)
                return ToUser(providerUser);

            if (emailUser == null)
                return null;

            if (string.IsNullOrWhiteSpace(emailUser.MicrosoftID))
            {
                emailUser = await _userRepository.UpdateProviderIdsAsync(
                    emailUser.Id,
                    null,
                    oauthUser.Id
                ) ?? emailUser;
            }

            return ToUser(emailUser);
        }

        private async Task<User> EnsureOAuthRoleAsync(User user)
        {
            user.Usertype = AuthRoles.NormalizeStored(user.Usertype);

            if (AuthRoles.IsKnownRole(user.Usertype))
                return user;

            var updatedUser = await _userRepository.UpdateUserAsync(user.Id, new User
            {
                Email = user.Email,
                Usertype = AuthRoles.DefaultOAuthRole,
            });

            if (updatedUser != null)
                return updatedUser;

            user.Usertype = AuthRoles.DefaultOAuthRole;
            return user;
        }

        private async Task<PendingOAuthSignupChallenge> CreatePendingOAuthSignupAsync(
            OAuthUser oauthUser,
            SessionTransport transport
        )
        {
            var signupToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var pendingState = new PendingOAuthSignupState
            {
                ProviderUserId = oauthUser.Id,
                Email = oauthUser.Email,
                Name = oauthUser.Name,
                Provider = oauthUser.Provider,
                Transport = transport,
            };

            var stored = await _cacheService.SetValueAsync(
                PendingOAuthSignupKey(signupToken),
                JsonConvert.SerializeObject(pendingState),
                PendingOAuthSignupTtl
            );

            if (!stored)
                throw new NotAvailableException();

            return new PendingOAuthSignupChallenge
            {
                SignupToken = signupToken,
                Email = oauthUser.Email,
                Name = oauthUser.Name,
                Provider = oauthUser.Provider,
            };
        }

        private async Task<PendingOAuthSignupState?> GetPendingOAuthSignupAsync(string signupToken)
        {
            var json = await _cacheService.GetValueAsync(PendingOAuthSignupKey(signupToken));
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonConvert.DeserializeObject<PendingOAuthSignupState>(json);
        }

        private static string PendingOAuthSignupKey(string signupToken) =>
            $"oauth:pending:{signupToken}";

        private async Task EnsureUserEnabledAsync(User user, bool revokeSessions = false)
        {
            if (!user.IsDisabled)
                return;

            if (revokeSessions)
                await _tokenService.RevokeAllRefreshSessionsAsync(user.Id);

            throw new ForbiddenException("This account is disabled.");
        }

        private static User ToUser(UserAuthRecord record)
        {
            return new User
            {
                Id = record.Id,
                Email = record.Email,
                Password = record.Password,
                Usertype = AuthRoles.NormalizeStored(record.Usertype),
                IsDisabled = record.IsDisabled,
                AuthVersion = record.AuthVersion,
            };
        }

        private static User ToUser(UserOAuthRecord record)
        {
            return new User
            {
                Id = record.Id,
                Email = record.Email,
                Usertype = AuthRoles.NormalizeStored(record.Usertype),
                GoogleID = record.GoogleID,
                MicrosoftID = record.MicrosoftID,
                IsDisabled = record.IsDisabled,
                AuthVersion = record.AuthVersion,
            };
        }

        private static VerificationOtpChallenge BuildPlaceholderForgotPasswordChallenge()
        {
            return new VerificationOtpChallenge
            {
                Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"),
                Challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            };
        }

        private sealed class PendingOAuthSignupState
        {
            public required string ProviderUserId { get; set; }
            public required string Email { get; set; }
            public required string Name { get; set; }
            public required string Provider { get; set; }
            public SessionTransport Transport { get; set; }
        }
    }
}
