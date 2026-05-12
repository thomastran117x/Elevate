using backend.main.features.payment.contracts.responses;
using backend.main.models.core;

namespace backend.main.features.payment
{
    public static class PaymentMapper
    {
        public static PaymentResponse MapToResponse(Payment payment) => new()
        {
            Id = payment.Id,
            UserId = payment.UserId,
            EventId = payment.EventId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            CheckoutUrl = payment.CheckoutUrl,
            CreatedAt = payment.CreatedAt
        };
    }
}
