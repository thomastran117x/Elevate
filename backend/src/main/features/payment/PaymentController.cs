using backend.main.application.security;
using backend.main.features.payment.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.payment
{
    /// <summary>
    /// Checkout, payment retrieval, refund, and Stripe webhook endpoints.
    /// </summary>
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
        [HttpPost("{eventId:int}")]
        [ProducesResponseType(typeof(ApiResponse<PaymentResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreatePaymentSession(int eventId)
        {
            try
            {
                var user = User.GetUserPayload();
                var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();

                var payment = await _paymentService.CreatePaymentSession(user.Id, user.Role, eventId, idempotencyKey);

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
        [HttpGet("{paymentId:int}")]
        [ProducesResponseType(typeof(ApiResponse<PaymentResponse>), StatusCodes.Status200OK)]
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
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<PaymentResponse>>), StatusCodes.Status200OK)]
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

        [AllowAnonymous]
        [HttpPost("webhook")]
        [RequestSizeLimit(65_536)]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> HandleWebhook()
        {
            try
            {
                var payload = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
                var signature = Request.Headers["Stripe-Signature"].ToString();

                await _paymentService.HandleWebhook(payload, signature);

                return Ok(new MessageResponse("Webhook processed successfully."));
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
        [HttpPost("{paymentId:int}/refund")]
        [ProducesResponseType(typeof(ApiResponse<PaymentResponse>), StatusCodes.Status200OK)]
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
