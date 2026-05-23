using System.Security.Claims;
using System.Text;

using backend.main.features.payment;
using backend.main.features.payment.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using PaymentEntity = backend.main.features.payment.Payment;

namespace backend.tests.Unit.Features.Payment;

public class PaymentControllerTests
{
    [Fact]
    public async Task CreatePaymentSession_ShouldPassIdempotencyHeader()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(s => s.CreatePaymentSession(7, "Organizer", 19, "idem-123"))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 4,
                UserId = 7,
                EventId = 19,
                Amount = 2500,
                Status = PaymentStatus.Pending,
                CheckoutUrl = "https://checkout.test/session-4"
            });

        var controller = CreateController(service.Object);
        controller.Request.Headers["Idempotency-Key"] = "idem-123";

        var result = await controller.CreatePaymentSession(19);

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<PaymentResponse>>().Subject;
        response.Data!.CheckoutUrl.Should().Be("https://checkout.test/session-4");
    }

    [Fact]
    public async Task GetPayment_ShouldForbidAccessToAnotherUsersPayment()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(s => s.GetPayment(4))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 4,
                UserId = 99,
                EventId = 19,
                Amount = 2500,
                Status = PaymentStatus.Pending
            });

        var controller = CreateController(service.Object);

        var result = await controller.GetPayment(4);

        var response = result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RefundPayment_ShouldReturnRefundedPayment()
    {
        var service = new Mock<IPaymentService>();
        service.Setup(s => s.RefundPayment(4, 7))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 4,
                UserId = 7,
                EventId = 19,
                Amount = 2500,
                Status = PaymentStatus.Refunded
            });

        var controller = CreateController(service.Object);

        var result = await controller.RefundPayment(4);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<PaymentResponse>>().Subject;
        response.Data!.Status.Should().Be(nameof(PaymentStatus.Refunded));
    }

    [Fact]
    public async Task HandleWebhook_ShouldForwardPayloadAndSignature()
    {
        var service = new Mock<IPaymentService>();
        var controller = CreateController(service.Object);
        controller.Request.Headers["Stripe-Signature"] = "sig_test";
        controller.ControllerContext.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"evt_1\"}"));

        var result = await controller.HandleWebhook();

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.HandleWebhook("{\"id\":\"evt_1\"}", "sig_test"), Times.Once);
    }

    private static PaymentController CreateController(IPaymentService service)
    {
        var controller = new PaymentController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "organizer@example.com"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
