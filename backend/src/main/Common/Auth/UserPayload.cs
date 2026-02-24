namespace backend.main.Common
{
    public class UserPayload
    {
        public int Id
        {
            get; set;
        }
        public string Email
        {
            get; set;
        }
        public string Role
        {
            get; set;
        }

        public UserPayload(int Id, string Email, string Role)
        {
            this.Id = Id;
            this.Email = Email;
            this.Role = Role;
        }
    }
}
