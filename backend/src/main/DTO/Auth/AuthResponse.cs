namespace backend.main.DTOs
{
    public class AuthResponse
    {
        public AuthResponse(int id, string username, string userType, string token)
        {
            Id = id;
            Username = username;
            Usertype = userType;
            Token = token;
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
        public string Avatar
        {
            get; set;
        }
    }
}
