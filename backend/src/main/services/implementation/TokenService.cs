using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using backend.main.configurations.environment;
using backend.main.exceptions.http;
using backend.main.models.core;
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
        private readonly TimeSpan REFRESH_TTL = TimeSpan.FromHours(1);
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

        public async Task<string> GenerateRefreshToken(int userId)
        {
            try
            {
                string token;

                do
                {
                    token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

                    string? existing = await _cacheService.GetValueAsync($"refresh:{token}");

                    if (existing == null)
                        break;
                }

                while (true);

                var result = await _cacheService.SetValueAsync(
                    key: $"refresh:{token}",
                    value: userId.ToString(),
                    expiry: REFRESH_TTL
                );

                if (!result)
                    throw new NotAvaliableException();

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

        public async Task<int> ValidateRefreshToken(string refreshToken)
        {
            try
            {
                string? storedValue = await _cacheService.GetValueAsync($"refresh:{refreshToken}");

                if (string.IsNullOrEmpty(storedValue))
                    throw new UnauthorizedException("Invalid or expired refresh token.");

                int userId;

                if (int.TryParse(storedValue, out userId))
                {
                    // OK
                }
                else
                {
                    try
                    {
                        var user = JsonConvert.DeserializeObject<User>(storedValue)
                            ?? throw new UnauthorizedException("Invalid refresh token payload.");

                        userId = user.Id;
                    }
                    catch
                    {
                        throw new UnauthorizedException("Invalid refresh token payload.");
                    }
                }


                var result = await _cacheService.DeleteKeyAsync($"refresh:{refreshToken}");
                if (!result)
                    throw new NotAvaliableException();

                return userId;
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
                    throw new NotAvaliableException();

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
    }
}
