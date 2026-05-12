namespace backend.main.features.auth.contracts
{
    public sealed class UserOAuthRecord
    {
        public int Id { get; init; }
        public string Email { get; init; } = null!;
        public string Usertype { get; init; } = null!;
        public string? GoogleID { get; init; }
        public string? MicrosoftID { get; init; }
        public bool IsDisabled { get; init; }
        public int AuthVersion { get; init; }
    }
}
