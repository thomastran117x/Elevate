namespace backend.main.application.security
{
    public class UserIdentityPayload
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }

        public UserIdentityPayload(int id, string email, string role)
        {
            Id = id;
            Email = email;
            Role = role;
        }
    }
}
