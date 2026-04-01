using backend.main.dtos;
using backend.main.dtos.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.publishers.interfaces;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

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

        public async Task<UserToken> LoginAsync(string email, string password)
        {
            try
            {
                User? user = await _userRepository.GetUserByEmailAsync(email);

                var hashToCheck = user?.Password ?? DummyHash;

                bool isValidPassword = VerifyPassword(password, hashToCheck);

                if (user == null || user.Password == null || !isValidPassword)
                    throw new UnauthorizedException("Invalid email or password");

                await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, _requestInfo);

                return await GenerateTokenPair(user);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] LoginAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task SignUpAsync(string email, string password, string userType)
        {
            try
            {
                var result = await _tokenService.VerificationTokenExist(email);
                if (result is not null)
                    return;

                if (await _userRepository.EmailExistsAsync(email))
                    throw new ConflictException($"An account is already registered with the email: {email}");

                string hashedPassword = HashPassword(password);

                User user = new User
                {
                    Email = email,
                    Password = hashedPassword,
                    Usertype = userType
                };

                var token = await _tokenService.GenerateVerificationToken(user);
                var message = new EmailMessage
                {
                    Type = EmailMessageType.VerifyEmail,
                    Email = email,
                    Token = token
                };

                await _publisher.PublishAsync("eventxperience-email", message);

                return;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[InternalServerErrorException] SignUpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyAsync(string token)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationToken(token);

                await _userRepository.CreateUserAsync(user);

                return await GenerateTokenPair(user);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task ForgotPasswordAsync(string email)
        {
            try
            {
                var existingEmail = await _userRepository.EmailExistsAsync(email);
                if (!existingEmail)
                    return;

                User user = new User
                {
                    Email = email,
                    Password = "placeholder",
                    Usertype = "placeholder"
                };

                var token = await _tokenService.GenerateVerificationToken(user);
                var message = new EmailMessage
                {
                    Type = EmailMessageType.ResetPassword,
                    Email = email,
                    Token = token
                };

                await _publisher.PublishAsync("eventxperience-email", message);

                return;
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
                var user = await _tokenService.VerifyVerificationToken(token);

                var hashedPassword = HashPassword(password);

                var existingUser = await _userRepository.GetUserByEmailAsync(user.Email)
                    ?? throw new UnauthorizedException("Invalid token");

                existingUser.Password = hashedPassword;

                await _userRepository.UpdateUserAsync(existingUser.Id, existingUser);

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

        public async Task<UserToken> GoogleAsync(string token)
        {
            try
            {
                OAuthUser oauthUser = await _oauthService.VerifyGoogleTokenAsync(token);
                if (oauthUser == null)
                    throw new UnauthorizedException("Invalid Google Token");

                var user =
                    await _userRepository.GetUserByEmailAsync(oauthUser.Email)
                    ?? await _userRepository.GetUserByGoogleIdAsync(oauthUser.Id);

                if (user == null)
                {
                    user = await _userRepository.CreateUserAsync(new User
                    {
                        Email = oauthUser.Email,
                        Usertype = "undefined",
                        GoogleID = oauthUser.Id,
                    });
                }

                return await GenerateTokenPair(user);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GoogleAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> MicrosoftAsync(string token)
        {
            try
            {
                OAuthUser oauthUser = await _oauthService.VerifyMicrosoftTokenAsync(token);
                if (oauthUser == null)
                    throw new UnauthorizedException("Invalid Microsoft Token");

                var user =
                    await _userRepository.GetUserByEmailAsync(oauthUser.Email)
                    ?? await _userRepository.GetUserByMicrosoftIdAsync(oauthUser.Id);

                if (user == null)
                {
                    user = await _userRepository.CreateUserAsync(new User
                    {
                        Email = oauthUser.Email,
                        Usertype = "undefined",
                        MicrosoftID = oauthUser.Id,
                    });
                }

                return await GenerateTokenPair(user);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] MicrosoftAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> HandleTokensAsync(string oldRefreshToken)
        {
            try
            {
                var userId = await _tokenService.ValidateRefreshToken(oldRefreshToken);
                var user = await _userRepository.GetUserAsync(userId);
                if (user == null)
                    throw new ResourceNotFoundException($"User with ID {userId} is not found");

                return await GenerateTokenPair(user);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HandleTokensAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyDeviceLoginAsync(string token)
        {
            try
            {
                return await _deviceService.VerifyDeviceAsync(token);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyDeviceLoginAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task HandleLogoutAsync(string refreshToken)
        {
            try
            {
                _ = await _tokenService.ValidateRefreshToken(refreshToken);
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
                return BCrypt.Net.BCrypt.HashPassword(password);
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

        private async Task<UserToken> GenerateTokenPair(User user)
        {
            try
            {
                var accessToken = _tokenService.GenerateAccessToken(user);
                var refreshToken = await _tokenService.GenerateRefreshToken(user.Id);
                Token authToken = new Token(accessToken, refreshToken);

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
    }
}
