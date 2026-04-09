namespace backend.main.dtos.responses.events
{
    public class BatchCreateResultResponse
    {
        public List<EventResponse> Created { get; set; } = new();
        public List<BatchCreateFailure> Failed { get; set; } = new();
    }

    public class BatchCreateFailure
    {
        public int Index { get; set; }
        public string Reason { get; set; } = null!;
    }
}
