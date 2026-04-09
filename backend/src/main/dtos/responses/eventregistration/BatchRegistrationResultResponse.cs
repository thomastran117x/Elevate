namespace backend.main.dtos.responses.eventregistration
{
    public class BatchRegistrationResultResponse
    {
        public List<int> Succeeded { get; set; } = new();
        public List<BatchRegistrationFailure> Failed { get; set; } = new();
    }

    public class BatchRegistrationFailure
    {
        public int EventId { get; set; }
        public string Reason { get; set; } = null!;
    }
}
