using backend.main.Models;

namespace backend.main.Common
{
    public class Token
    {
        public string AccessToken
        {
            get; set;
        }
        public string RefreshToken
        {
            get; set;
        }

        public Token(string accessToken, string refreshToken)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
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
