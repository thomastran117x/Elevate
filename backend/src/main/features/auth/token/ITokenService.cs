using backend.main.features.profile;
using backend.main.shared.requests;

namespace backend.main.features.auth.token
{
    public interface ITokenService
    {
        public AccessTokenIssue GenerateAccessToken(User user, string sessionId);
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
        public Task<TimeSpan?> GetRefreshSessionTtlAsync(string sessionId);
        public Task RevokeRefreshSessionAsync(string sessionId);
        public Task RevokeAllRefreshSessionsAsync(int userId);
        public Task<string?> VerificationTokenExist(string email, VerificationPurpose purpose);
    }
}


