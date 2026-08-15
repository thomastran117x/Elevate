namespace backend.main.features.auth.contracts
{
    public sealed class UserRecoveryRecord
    {
        public int Id { get; init; }
        public required string Email { get; init; }
        public string? Username { get; init; }
        public string? RecipientName { get; init; }
        public bool IsDisabled { get; init; }
        public bool HasLocalPassword { get; init; }
        public bool HasGoogleProvider { get; init; }
        public bool HasMicrosoftProvider { get; init; }

        public IReadOnlyList<string> SignInProviders
        {
            get
            {
                var providers = new List<string>();
                if (HasGoogleProvider)
                    providers.Add("Google");
                if (HasMicrosoftProvider)
                    providers.Add("Microsoft");
                return providers;
            }
        }
    }
}
