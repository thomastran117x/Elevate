using backend.main.dtos;
using backend.main.configurations.security;
using backend.main.dtos.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;
using System.Security.Cryptography;

namespace backend.main.services.implementation
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IOAuthService _oauthService;
        private readonly ITokenService _tokenService;
        private readonly IPublisher _publisher;
        private readonly IDeviceService _deviceService;
        private readonly ClientRequestInfo _requestInfo;
        private const string DummyHash = "$2a$11$9FJqO6j/4jP3E2fOQdWgMuKZXWWvPZ09f8Pj0L9VqB6TfqZ4fE5SO";

        public AuthService(IUserRepository userRepository, IOAuthService oauthService, ITokenService tokenService, IPublisher publisher, IDeviceService deviceService, ClientRequestInfo requestInfo)
        {
            _userRepository = userRepository;
            _oauthService = oauthService;
            _tokenService = tokenService;
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
                User? user = await _userRepository.GetUserByEmailAsync(email);

                var hashToCheck = user?.Password ?? DummyHash;

                bool isValidPassword = VerifyPassword(password, hashToCheck);

                if (user == null || user.Password == null || !isValidPassword)
                    throw new UnauthorizedException("Invalid email or password");

                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return await GenerateTokenPair(user, transport, rememberMe: rememberMe);
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
                var existingEmail = await _userRepository.EmailExistsAsync(email);
                if (!existingEmail)
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

        public async Task<UserToken> GoogleAsync(
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
                {
                    user = await _userRepository.CreateUserAsync(new User
                    {
                        Email = oauthUser.Email,
                        Usertype = AuthRoles.DefaultOAuthRole,
                        GoogleID = oauthUser.Id,
                    });
                }
                else
                {
                    user = await EnsureOAuthRoleAsync(user);
                }

                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return await GenerateTokenPair(user, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GoogleAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> MicrosoftAsync(string token, SessionTransport transport)
        {
            try
            {
                OAuthUser oauthUser = await _oauthService.VerifyMicrosoftTokenAsync(token);
                if (oauthUser == null)
                    throw new UnauthorizedException("Invalid Microsoft Token");

                var user = await ResolveMicrosoftUserAsync(oauthUser);

                if (user == null)
                {
                    user = await _userRepository.CreateUserAsync(new User
                    {
                        Email = oauthUser.Email,
                        Usertype = AuthRoles.DefaultOAuthRole,
                        MicrosoftID = oauthUser.Id,
                    });
                }
                else
                {
                    user = await EnsureOAuthRoleAsync(user);
                }

                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return await GenerateTokenPair(user, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] MicrosoftAsync failed: {e}");
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
                    accessToken,
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

            var existingUser = await _userRepository.GetUserByEmailAsync(email)
                ?? throw new UnauthorizedException("Invalid token");

            existingUser.Password = hashedPassword;

            await _userRepository.UpdateUserAsync(existingUser.Id, existingUser);
            await _tokenService.RevokeAllRefreshSessionsAsync(existingUser.Id);
        }

        private async Task<User?> ResolveGoogleUserAsync(OAuthUser oauthUser)
        {
            var providerUser = await _userRepository.GetUserByGoogleIdAsync(oauthUser.Id);
            var emailUser = await _userRepository.GetUserByEmailAsync(oauthUser.Email);

            if (providerUser != null && emailUser != null && providerUser.Id != emailUser.Id)
                throw new ConflictException("This Google account is already linked to another user.");

            if (providerUser != null)
                return providerUser;

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

            return emailUser;
        }

        private async Task<User?> ResolveMicrosoftUserAsync(OAuthUser oauthUser)
        {
            var providerUser = await _userRepository.GetUserByMicrosoftIdAsync(oauthUser.Id);
            var emailUser = await _userRepository.GetUserByEmailAsync(oauthUser.Email);

            if (providerUser != null && emailUser != null && providerUser.Id != emailUser.Id)
                throw new ConflictException("This Microsoft account is already linked to another user.");

            if (providerUser != null)
                return providerUser;

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

            return emailUser;
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

        private static VerificationOtpChallenge BuildPlaceholderForgotPasswordChallenge()
        {
            return new VerificationOtpChallenge
            {
                Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"),
                Challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            };
        }
    }
}
