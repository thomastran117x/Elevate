namespace backend.main.dtos.responses.payment
{
    public class PaymentResponse
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int EventId { get; set; }
        public long Amount { get; set; }
        public string Currency { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? CheckoutUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
