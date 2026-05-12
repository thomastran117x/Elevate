using backend.main.models.core;
using backend.main.models.enums;

namespace backend.main.features.payment
{
    public interface IPaymentRepository
    {
        /// <summary>
        /// Atomically checks for an active (Pending or Succeeded) payment for the given
        /// user/event pair and inserts <paramref name="stub"/> if none exists.
        /// Returns the existing payment when one is found, or the newly inserted stub.
        /// </summary>
        Task<Payment> GetOrCreateActiveAsync(int userId, int eventId, Payment stub);
        Task<Payment?> GetByIdAsync(int id);
        Task<Payment?> GetByIdempotencyKeyAsync(string idempotencyKey);
        Task<Payment?> GetByExternalSessionIdAsync(string sessionId);
        Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int pageSize = 20);
        Task<Payment?> UpdateCheckoutDetailsAsync(int id, string sessionId, string checkoutUrl);
        Task<Payment?> UpdateStatusAsync(int id, PaymentStatus status, string? externalPaymentIntentId);
    }
}
