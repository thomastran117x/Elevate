namespace backend.main.dtos.requests.auth
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
