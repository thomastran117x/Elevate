namespace backend.main.features.auth.contracts
{
    public sealed class UserStatusRecord
    {
        public int Id
        {
            get; init;
        }
        public bool IsDisabled
        {
            get; init;
        }
        public DateTime? DisabledAtUtc
        {
            get; init;
        }
        public string? DisabledReason
        {
            get; init;
        }
        public int AuthVersion
        {
            get; init;
        }
        /// <summary>
        /// Whether this update moved the account between enabled and disabled. An update that sets
        /// the state it already had (a retry, or a new reason) is still written, but changes nothing
        /// the owner needs to be told about.
        /// </summary>
        public bool StatusChanged
        {
            get; init;
        }
    }
}
