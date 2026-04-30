namespace backend.main.dtos.responses.auth
{
    public class CurrentUserResponse
    {
        public int Id
        {
            get; set;
        }
        public string Email
        {
            get; set;
        } = null!;
        public string Username
        {
            get; set;
        } = null!;
        public string? Name
        {
            get; set;
        }
        public string? Avatar
        {
            get; set;
        }
        public string Usertype
        {
            get; set;
        } = null!;
    }
}
