namespace backend.main.features.auth.contracts
{
    public sealed class UserOAuthRecord
    {
        public int Id
        {
            get; init;
        }
        public string Email { get; init; } = null!;
        public string Usertype { get; init; } = null!;

        /// <summary>Whether the account links any provider identity.</summary>
        public bool HasProviderLink
        {
            get; init;
        }

        /// <summary>The link a provider-subject lookup matched; null for other lookups.</summary>
        public int? LinkId
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
