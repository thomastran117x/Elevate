using backend.main.Common;
using backend.main.Models;

namespace backend.main.Interfaces
{
    public interface ITokenService
    {
        public string GenerateAccessToken(User user);
        public Task<string> GenerateRefreshToken(int userId);
        public Task<string> GenerateVerificationToken(User user);
        public Task<User> VerifyVerificationToken(string verifyToken);
        public Task<int> ValidateRefreshToken(string refreshToken);
        public Task<string?> VerificationTokenExist(string email);
    }
}
