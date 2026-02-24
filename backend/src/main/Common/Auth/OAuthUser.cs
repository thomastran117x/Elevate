namespace backend.main.Common
{
    public class OAuthUser
    {
        public string Id
        {
            get; set;
        }
        public string Email
        {
            get; set;
        }
        public string Name
        {
            get; set;
        }
        public string Provider
        {
            get; set;
        }
        public OAuthUser(string Id, string Email, string Name, string Provider)
        {
            this.Id = Id;
            this.Email = Email;
            this.Name = Name;
            this.Provider = Provider;
        }
    }
}
