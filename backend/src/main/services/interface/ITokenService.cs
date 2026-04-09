using backend.main.dtos.general;
using backend.main.models.core;
using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface ITokenService
    {
        public string GenerateAccessToken(User user);
        public Task<string> GenerateRefreshToken(
            int userId,
            ClientRequestInfo requestInfo,
            string? sessionId = null
        );
        public Task<string> GenerateVerificationToken(User user);
        public Task<User> VerifyVerificationToken(string verifyToken);
        public Task<RefreshTokenValidationResult> ValidateRefreshToken(
            string refreshToken,
            ClientRequestInfo requestInfo
        );
        public Task RevokeRefreshSessionAsync(string sessionId);
        public Task RevokeAllRefreshSessionsAsync(int userId);
        public Task<string?> VerificationTokenExist(string email);
    }
}
