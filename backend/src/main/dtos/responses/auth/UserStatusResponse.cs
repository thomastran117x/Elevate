namespace backend.main.dtos.responses.auth
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
