namespace backend.main.features.auth.contracts.requests
{
    public class UpdateUserStatusRequest
    {
        public bool IsDisabled
        {
            get; set;
        }
        [RequestText(RequestTextKind.HumanMultiline)]
        public string? Reason
        {
            get; set;
        }
    }
}
