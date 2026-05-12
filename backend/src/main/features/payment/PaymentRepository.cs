using System.Data;

using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.payment
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly AppDatabaseContext _context;

        public PaymentRepository(AppDatabaseContext context) => _context = context;

        public async Task<Payment> GetOrCreateActiveAsync(int userId, int eventId, Payment stub)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var existing = await _context.Payments
                    .FirstOrDefaultAsync(p =>
                        p.UserId == userId &&
                        p.EventId == eventId &&
                        (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Succeeded));

                if (existing != null)
                {
                    await transaction.CommitAsync();
                    return existing;
                }

                _context.Payments.Add(stub);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return stub;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<Payment?> GetByIdAsync(int id)
        {
            return await _context.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<Payment?> GetByIdempotencyKeyAsync(string idempotencyKey)
        {
            return await _context.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey);
        }

        public async Task<Payment?> GetByExternalSessionIdAsync(string sessionId)
        {
            return await _context.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ExternalSessionId == sessionId);
        }

        public async Task<List<Payment>> GetByUserIdAsync(int userId, int page = 1, int pageSize = 20)
        {
            return await _context.Payments
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<Payment?> UpdateCheckoutDetailsAsync(int id, string sessionId, string checkoutUrl)
        {
            var payment = await _context.Payments.FindAsync(id);
            if (payment == null)
                return null;

            payment.ExternalSessionId = sessionId;
            payment.CheckoutUrl = checkoutUrl;
            payment.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task<Payment?> UpdateStatusAsync(int id, PaymentStatus status, string? externalPaymentIntentId)
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
        }
    }
}


