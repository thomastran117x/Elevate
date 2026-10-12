namespace backend.main.features.auth.contracts
{
    public sealed class UserAuthRecord
    {
        public int Id
        {
            get; init;
        }
        public string Email { get; init; } = null!;
        /// <summary>Carried so a new password can be checked against the account's own name.</summary>
        public string? Username
        {
            get; init;
        }
        public string? Password
        {
            get; init;
        }
        public string Usertype { get; init; } = null!;
        public string? Name
        {
            get; init;
        }
        public bool IsDisabled
        {
            get; init;
        }
        public int AuthVersion
        {
            get; init;
        }
    }
}
