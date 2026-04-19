namespace backend.main.dtos.messages
{
    public sealed record EventIndexEvent
    {
        public required string Operation { get; init; }
        public required int EventId { get; init; }
        public int? ClubId { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Location { get; init; }
        public bool? IsPrivate { get; init; }
        public DateTime? StartTime { get; init; }
        public DateTime? EndTime { get; init; }
        public DateTime? CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
}
