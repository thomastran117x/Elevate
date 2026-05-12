namespace backend.main.features.profile.contracts
{
    public sealed class UserProfileRecord
    {
        public int Id { get; init; }
        public string Email { get; init; } = null!;
        public string Username { get; init; } = null!;
        public string? Name { get; init; }
        public string? Avatar { get; init; }
        public string Usertype { get; init; } = null!;
    }
}
