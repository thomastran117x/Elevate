namespace backend.main.dtos.responses.auth
{
    public class AuthResponse
    {
        public AuthResponse(
            int id,
            string username,
            string userType,
            string token,
            string? refreshToken = null,
            string? sessionBindingToken = null
        )
        {
            Id = id;
            Username = username;
            Usertype = userType;
            Token = token;
            AccessToken = token;
            RefreshToken = refreshToken;
            SessionBindingToken = sessionBindingToken;
            Avatar = "placeholder";
        }

        public int Id
        {
            get; set;
        }
        public string Username
        {
            get; set;
        }
        public string Usertype
        {
            get; set;
        }
        public string Token
        {
            get; set;
        }
        public string AccessToken
        {
            get; set;
        }
        public string? RefreshToken
        {
            get; set;
        }
        public string? SessionBindingToken
        {
            get; set;
        }
        public string Avatar
        {
            get; set;
        }
    }
}
