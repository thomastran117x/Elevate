using backend.main.features.auth.events;
using backend.main.features.profile;
using backend.main.shared.requests;

namespace backend.main.features.auth.token
{
    public interface ITokenService
    {
        public AccessTokenIssue GenerateAccessToken(User user, string sessionId);
        public Task<RefreshTokenIssue> GenerateRefreshToken(
            int userId,
            int authVersion,
            ClientRequestInfo requestInfo,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        );
        public Task<string> GenerateVerificationToken(User user, VerificationPurpose purpose);
        /// <param name="beforeConsume">
        /// Runs once the token is proven valid but before it is used up. An exception thrown here
        /// leaves the token in place, so a request rejected for another reason can be retried.
        /// </param>
        public Task<User> VerifyVerificationToken(
            string verifyToken,
            VerificationPurpose expectedPurpose,
            Func<User, Task>? beforeConsume = null);
        public Task<VerificationOtpChallenge> GenerateVerificationOtpAsync(
            User user,
            VerificationPurpose purpose
        );
        /// <param name="beforeConsume">
        /// Runs once the code is proven correct but before the challenge is used up, as for
        /// <see cref="VerifyVerificationToken"/>.
        /// </param>
        public Task<User> VerifyVerificationOtpAsync(
            string code,
            string challenge,
            VerificationPurpose expectedPurpose,
            Func<User, Task>? beforeConsume = null
        );
        public Task<VerificationArtifacts> GenerateVerificationArtifactsAsync(
            User user,
            VerificationPurpose purpose,
            bool replaceExisting = false
        );
        public Task<RefreshTokenValidationResult> ValidateRefreshToken(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport expectedTransport,
            ClientRequestInfo requestInfo
        );
        /// <summary>
        /// Stores a step-up proof only while the refresh session still exists and the user's
        /// step-up generation still equals <paramref name="expectedGeneration"/>, in one atomic
        /// operation, so a verification that races logout or a factor change cannot outlive it.
        /// </summary>
        public Task<StepUpProofWriteResult> StoreStepUpProofAsync(
            string sessionId,
            int userId,
            long expectedGeneration,
            string proofJson,
            TimeSpan lifetime
        );
        public Task RevokeRefreshSessionAsync(string sessionId, AuthSecurityReason reason);
        /// <param name="actorUserId">The account that caused the revocation when it is not
        /// <paramref name="userId"/>, such as an administrator.</param>
        public Task RevokeAllRefreshSessionsAsync(int userId, AuthSecurityReason reason, int? actorUserId = null);
        public Task<string?> VerificationTokenExist(string email, VerificationPurpose purpose);
        public Task<VerificationArtifacts> GenerateEmailChangeArtifactsAsync(
            int userId,
            int authVersion,
            string newEmail
        );
        public Task<PendingEmailChange> ConsumeEmailChangeTokenAsync(string token);
        public Task<PendingEmailChange> ConsumeEmailChangeOtpAsync(string code, string challenge);
        public Task<PendingEmailChange?> GetPendingEmailChangeAsync(int userId);
        public Task CancelPendingEmailChangeAsync(int userId);
    }
}

