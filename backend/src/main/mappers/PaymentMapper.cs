using backend.main.dtos.responses.payment;
using backend.main.models.core;

namespace backend.main.Mappers
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
