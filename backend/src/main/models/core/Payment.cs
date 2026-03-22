using backend.main.models.enums;

namespace backend.main.models.core
{
    public class Payment
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int EventId { get; set; }
        public long Amount { get; set; }
        public string Currency { get; set; } = "usd";
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public string? ExternalSessionId { get; set; }
        public string? ExternalPaymentIntentId { get; set; }
        public string? CheckoutUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
