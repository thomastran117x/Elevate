using backend.main.configurations.resource.database;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.repositories.interfaces;

using Microsoft.EntityFrameworkCore;

namespace backend.main.repositories.implementation
{
    public class PaymentRepository : BaseRepository, IPaymentRepository
    {
        public PaymentRepository(AppDatabaseContext context) : base(context) { }

        public async Task<Payment> CreateAsync(Payment payment)
        {
            return await ExecuteAsync(async () =>
            {
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
                return payment;
            })!;
        }

        public async Task<Payment?> GetByIdAsync(int id)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Payments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id);
            });
        }

        public async Task<Payment?> GetByExternalSessionIdAsync(string sessionId)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Payments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ExternalSessionId == sessionId);
            });
        }

        public async Task<Payment?> GetByUserAndEventAsync(int userId, int eventId)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Payments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == userId && p.EventId == eventId);
            });
        }

        public async Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int pageSize = 20)
        {
            return await ExecuteAsync(async () =>
            {
                return await _context.Payments
                    .AsNoTracking()
                    .Where(p => p.UserId == userId)
                    .OrderByDescending(p => p.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            })!;
        }

        public async Task<Payment?> UpdateStatusAsync(int id, PaymentStatus status, string? externalPaymentIntentId)
        {
            return await ExecuteAsync(async () =>
            {
                var payment = await _context.Payments.FindAsync(id);
                if (payment == null)
                    return null;

                payment.Status = status;
                if (externalPaymentIntentId != null)
                    payment.ExternalPaymentIntentId = externalPaymentIntentId;
                payment.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return payment;
            });
        }
    }
}
