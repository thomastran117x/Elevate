using backend.main.features.auth.token;
using backend.main.features.profile;

namespace backend.main.features.auth
{
    public sealed class AuthenticatedSessionResult
    {
        public required UserToken UserToken
        {
            get; init;
        }
        public string? ReturnPath
        {
            get; init;
        }

        public User user => UserToken.user;
        public Token token => UserToken.token;
    }
}
