using backend.main.models.core;
using backend.main.models.enums;

namespace backend.main.repositories.interfaces
{
    public interface IPaymentRepository
    {
        Task<Payment> CreateAsync(Payment payment);
        Task<Payment?> GetByIdAsync(int id);
        Task<Payment?> GetByExternalSessionIdAsync(string sessionId);
        Task<Payment?> GetByUserAndEventAsync(int userId, int eventId);
        Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int pageSize = 20);
        Task<Payment?> UpdateStatusAsync(int id, PaymentStatus status, string? externalPaymentIntentId);
    }
}
