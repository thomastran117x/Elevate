using backend.main.features.auth.token;
using backend.main.features.profile;

namespace backend.main.features.auth
{
    public interface IAuthSessionService
    {
        Task<UserToken> IssueAsync(
            User user,
            SessionTransport transport,
            string? sessionId = null,
            bool? rememberMe = null
        );
    }
}
