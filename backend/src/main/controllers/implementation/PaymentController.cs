using backend.main.configurations.security;
using backend.main.dtos.responses.general;
using backend.main.dtos.responses.payment;
using backend.main.exceptions.http;
using backend.main.Mappers;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public PaymentController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        [Authorize]
        [HttpPost("{eventId}")]
        public async Task<IActionResult> CreatePaymentSession(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();
                var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();

                var payment = await _paymentService.CreatePaymentSession(user.Id, eventId, idempotencyKey);

                return StatusCode(201,
                    new ApiResponse<PaymentResponse>(
                        "Payment session created. Redirect to CheckoutUrl to complete payment.",
                        PaymentMapper.MapToResponse(payment)
                    ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[PaymentController] CreatePaymentSession failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("{paymentId}")]
        public async Task<IActionResult> GetPayment(int paymentId)
        {
            try
            {
                var user = User.GetUserPayload();
                var payment = await _paymentService.GetPayment(paymentId);

                if (payment.UserId != user.Id)
                    throw new ForbiddenException("Not allowed.");

                return Ok(new ApiResponse<PaymentResponse>(
                    $"Payment {paymentId} fetched successfully.",
                    PaymentMapper.MapToResponse(payment)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[PaymentController] GetPayment failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetMyPayments(int page = 1, int pageSize = 20)
        {
            try
            {
                var user = User.GetUserPayload();
                var payments = await _paymentService.GetPaymentsByUser(user.Id, page, pageSize);

                return Ok(new ApiResponse<IEnumerable<PaymentResponse>>(
                    "Payments fetched successfully.",
                    payments.Select(PaymentMapper.MapToResponse)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[PaymentController] GetMyPayments failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPost("webhook")]
        [RequestSizeLimit(65_536)]
        public async Task<IActionResult> HandleWebhook()
        {
            try
            {
                var payload = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
                var signature = Request.Headers["Stripe-Signature"].ToString();

                await _paymentService.HandleWebhook(payload, signature);

                return Ok();
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[PaymentController] HandleWebhook failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [Authorize]
        [HttpPost("{paymentId}/refund")]
        public async Task<IActionResult> RefundPayment(int paymentId)
        {
            try
            {
                var user = User.GetUserPayload();
                var payment = await _paymentService.RefundPayment(paymentId, user.Id);

                return Ok(new ApiResponse<PaymentResponse>(
                    $"Payment {paymentId} refunded successfully.",
                    PaymentMapper.MapToResponse(payment)
                ));
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[PaymentController] RefundPayment failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
