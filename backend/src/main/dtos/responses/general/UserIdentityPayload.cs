namespace backend.main.dtos.general
{
    public class UserIdentityPayload
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

        public UserIdentityPayload(int Id, string Email, string Role)
        {
            this.Id = Id;
            this.Email = Email;
            this.Role = Role;
        }
    }
}
