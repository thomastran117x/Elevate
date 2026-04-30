using backend.main.dtos.general;
using backend.main.models.core;
using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface ITokenService
    {
        public AccessTokenIssue GenerateAccessToken(User user);
        public Task<RefreshTokenIssue> GenerateRefreshToken(
            int userId,
            ClientRequestInfo requestInfo,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        );
        public Task<string> GenerateVerificationToken(User user, VerificationPurpose purpose);
        public Task<User> VerifyVerificationToken(string verifyToken, VerificationPurpose expectedPurpose);
        public Task<VerificationOtpChallenge> GenerateVerificationOtpAsync(
            User user,
            VerificationPurpose purpose
        );
        public Task<User> VerifyVerificationOtpAsync(
            string code,
            string challenge,
            VerificationPurpose expectedPurpose
        );
        public Task<VerificationArtifacts> GenerateVerificationArtifactsAsync(
            User user,
            VerificationPurpose purpose
        );
        public Task<RefreshTokenValidationResult> ValidateRefreshToken(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport expectedTransport,
            ClientRequestInfo requestInfo
        );
        public Task RevokeRefreshSessionAsync(string sessionId);
        public Task RevokeAllRefreshSessionsAsync(int userId);
        public Task<string?> VerificationTokenExist(string email, VerificationPurpose purpose);
    }
}
