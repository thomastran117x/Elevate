using backend.main.models.core;

namespace backend.main.services.interfaces
{
    public interface IPaymentService
    {
        Task<Payment> CreatePaymentSession(int userId, int eventId);
        Task<Payment> GetPayment(int paymentId);
        Task<List<Payment>> GetPaymentsByUser(int userId, int page = 1, int pageSize = 20);
        Task HandleWebhook(string payload, string signature);
        Task<Payment> RefundPayment(int paymentId, int requestingUserId);
    }
}
