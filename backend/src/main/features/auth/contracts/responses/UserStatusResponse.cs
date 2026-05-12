namespace backend.main.features.auth.contracts.responses
{
    public class UserStatusResponse
    {
        public int Id
        {
            get; set;
        }
        public bool IsDisabled
        {
            get; set;
        }
        public DateTime? DisabledAtUtc
        {
            get; set;
        }
        public string? DisabledReason
        {
            get; set;
        }
        public int AuthVersion
        {
            get; set;
        }
    }
}
