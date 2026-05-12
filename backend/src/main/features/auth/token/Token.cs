using backend.main.models.core;

namespace backend.main.features.auth.token
{
    public class Token
    {
        public string AccessToken
        {
            get; set;
        }
        public DateTime AccessTokenExpiresAtUtc
        {
            get; set;
        }
        public string RefreshToken
        {
            get; set;
        }
        public string SessionBindingToken
        {
            get; set;
        }
        public TimeSpan RefreshTokenLifetime
        {
            get; set;
        }
        public SessionTransport Transport
        {
            get; set;
        }

        public Token(
            string accessToken,
            DateTime accessTokenExpiresAtUtc,
            string refreshToken,
            string sessionBindingToken,
            TimeSpan refreshTokenLifetime,
            SessionTransport transport
        )
        {
            AccessToken = accessToken;
            AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc;
            RefreshToken = refreshToken;
            SessionBindingToken = sessionBindingToken;
            RefreshTokenLifetime = refreshTokenLifetime;
            Transport = transport;
        }
    }

    public class AccessTokenIssue
    {
        public string Value
        {
            get; set;
        }
        public DateTime ExpiresAtUtc
        {
            get; set;
        }

        public AccessTokenIssue(string value, DateTime expiresAtUtc)
        {
            Value = value;
            ExpiresAtUtc = expiresAtUtc;
        }
    }

    public class RefreshTokenIssue
    {
        public string Value
        {
            get; set;
        }
        public string SessionBindingToken
        {
            get; set;
        }
        public TimeSpan Lifetime
        {
            get; set;
        }
        public SessionTransport Transport
        {
            get; set;
        }

        public RefreshTokenIssue(
            string value,
            string sessionBindingToken,
            TimeSpan lifetime,
            SessionTransport transport
        )
        {
            Value = value;
            SessionBindingToken = sessionBindingToken;
            Lifetime = lifetime;
            Transport = transport;
        }
    }

    public class UserToken
    {
        public Token token
        {
            get; set;
        }
        public User user
        {
            get; set;
        }
        public UserToken(Token token, User user)
        {
            this.token = token;
            this.user = user;
        }
    }
}
