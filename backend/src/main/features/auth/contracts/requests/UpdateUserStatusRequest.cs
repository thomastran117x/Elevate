namespace backend.main.features.auth.contracts.requests
{
    public class UpdateUserStatusRequest
    {
        public bool IsDisabled
        {
            get; set;
        }
        public string? Reason
        {
            get; set;
        }
    }
}
