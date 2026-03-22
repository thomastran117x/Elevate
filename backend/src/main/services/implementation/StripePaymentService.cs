using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.enums;
using backend.main.repositories.interfaces;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Stripe;
using Stripe.Checkout;

namespace backend.main.services.implementation
{
    public class StripePaymentService : IPaymentService
    {
        private readonly IPaymentRepository _paymentRepository;
        private readonly IEventsService _eventsService;
        private readonly string _webhookSecret;

        public StripePaymentService(
            IPaymentRepository paymentRepository,
            IEventsService eventsService)
        {
            _paymentRepository = paymentRepository;
            _eventsService = eventsService;

            StripeConfiguration.ApiKey = Environment.GetEnvironmentVariable("STRIPE_API_KEY")
                ?? throw new InvalidOperationException("STRIPE_API_KEY is not configured.");

            _webhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET")
                ?? throw new InvalidOperationException("STRIPE_WEBHOOK_SECRET is not configured.");
        }

        public async Task<Payment> CreatePaymentSession(int userId, int eventId)
        {
            try
            {
                var ev = await _eventsService.GetEvent(eventId);

                if (ev.registerCost == 0)
                    throw new BadRequestException("This event is free and does not require payment.");

                var existing = await _paymentRepository.GetByUserAndEventAsync(userId, eventId);
                if (existing != null && existing.Status == PaymentStatus.Succeeded)
                    throw new ConflictException("You have already paid for this event.");

                var successUrl = Environment.GetEnvironmentVariable("STRIPE_SUCCESS_URL")
                    ?? "http://localhost:4200/payment/success";
                var cancelUrl = Environment.GetEnvironmentVariable("STRIPE_CANCEL_URL")
                    ?? "http://localhost:4200/payment/cancel";

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                Currency = "usd",
                                UnitAmount = ev.registerCost,
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = ev.Name,
                                    Description = ev.Description
                                }
                            },
                            Quantity = 1
                        }
                    },
                    Mode = "payment",
                    SuccessUrl = successUrl + "?session_id={CHECKOUT_SESSION_ID}",
                    CancelUrl = cancelUrl,
                    Metadata = new Dictionary<string, string>
                    {
                        { "userId", userId.ToString() },
                        { "eventId", eventId.ToString() }
                    }
                };

                var sessionService = new SessionService();
                var session = await sessionService.CreateAsync(options);

                var payment = new Payment
                {
                    UserId = userId,
                    EventId = eventId,
                    Amount = ev.registerCost,
                    Currency = "usd",
                    Status = PaymentStatus.Pending,
                    ExternalSessionId = session.Id,
                    CheckoutUrl = session.Url
                };

                return await _paymentRepository.CreateAsync(payment);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[StripePaymentService] CreatePaymentSession failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Payment> GetPayment(int paymentId)
        {
            try
            {
                return await _paymentRepository.GetByIdAsync(paymentId)
                    ?? throw new ResourceNotFoundException($"Payment {paymentId} not found.");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[StripePaymentService] GetPayment failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<List<Payment>> GetPaymentsByUser(int userId, int page = 1, int pageSize = 20)
        {
            try
            {
                return await _paymentRepository.GetByUserIdAsync(userId, page, pageSize);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[StripePaymentService] GetPaymentsByUser failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task HandleWebhook(string payload, string signature)
        {
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(payload, signature, _webhookSecret);

                if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
                {
                    var session = stripeEvent.Data.Object as Session;
                    if (session == null) return;

                    var payment = await _paymentRepository.GetByExternalSessionIdAsync(session.Id);
                    if (payment == null) return;

                    await _paymentRepository.UpdateStatusAsync(
                        payment.Id,
                        PaymentStatus.Succeeded,
                        session.PaymentIntentId);
                }
                else if (stripeEvent.Type == EventTypes.CheckoutSessionExpired)
                {
                    var session = stripeEvent.Data.Object as Session;
                    if (session == null) return;

                    var payment = await _paymentRepository.GetByExternalSessionIdAsync(session.Id);
                    if (payment == null) return;

                    await _paymentRepository.UpdateStatusAsync(payment.Id, PaymentStatus.Failed, null);
                }
            }
            catch (StripeException e)
            {
                Logger.Error($"[StripePaymentService] Webhook signature verification failed: {e}");
                throw new BadRequestException("Invalid webhook signature.");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[StripePaymentService] HandleWebhook failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<Payment> RefundPayment(int paymentId, int requestingUserId)
        {
            try
            {
                var payment = await GetPayment(paymentId);

                if (payment.UserId != requestingUserId)
                    throw new ForbiddenException("Not allowed.");

                if (payment.Status != PaymentStatus.Succeeded)
                    throw new BadRequestException("Only succeeded payments can be refunded.");

                if (string.IsNullOrEmpty(payment.ExternalPaymentIntentId))
                    throw new InternalServerErrorException("Payment intent ID not found.");

                var refundService = new RefundService();
                await refundService.CreateAsync(new RefundCreateOptions
                {
                    PaymentIntent = payment.ExternalPaymentIntentId
                });

                return await _paymentRepository.UpdateStatusAsync(
                    payment.Id,
                    PaymentStatus.Refunded,
                    payment.ExternalPaymentIntentId)
                    ?? throw new InternalServerErrorException("Refund status update failed.");
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[StripePaymentService] RefundPayment failed: {e}");
                throw new InternalServerErrorException();
            }
        }
    }
}
