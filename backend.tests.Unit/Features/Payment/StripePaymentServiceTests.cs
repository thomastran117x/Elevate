using backend.main.features.events;
using backend.main.features.payment;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using Moq;

using PaymentEntity = backend.main.features.payment.Payment;

namespace backend.tests.Unit.Features.Payment;

public class StripePaymentServiceTests
{
    [Fact]
    public async Task CreatePaymentSession_ShouldReturnExistingIdempotentPayment()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(repo => repo.GetByIdempotencyKeyAsync("idem-1"))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 7,
                UserId = 9,
                EventId = 15,
                Amount = 2000,
                Status = PaymentStatus.Pending,
                CheckoutUrl = "https://checkout.test/existing"
            });

        var service = CreateService(repository: repository);

        var result = await service.CreatePaymentSession(9, "Participant", 15, "idem-1");

        result.Id.Should().Be(7);
        repository.Verify(repo => repo.GetOrCreateActiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PaymentEntity>()), Times.Never);
    }

    [Fact]
    public async Task CreatePaymentSession_ShouldRejectFreeEvents()
    {
        var eventsService = new Mock<IEventsService>();
        eventsService.Setup(service => service.EnsureCanViewEventAsync(15, 9, "Participant"))
            .Returns(Task.CompletedTask);
        eventsService.Setup(service => service.GetEvent(15))
            .ReturnsAsync(new backend.main.features.events.Events
            {
                Id = 15,
                Name = "Free Event",
                Description = "No charge",
                Location = "Campus",
                StartTime = DateTime.UtcNow.AddDays(1),
                ClubId = 4,
                registerCost = 0
            });

        var service = CreateService(eventsService: eventsService);

        var act = () => service.CreatePaymentSession(9, "Participant", 15);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("This event is free and does not require payment.");
    }

    [Fact]
    public async Task CreatePaymentSession_ShouldRejectAlreadySucceededActivePayment()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(repo => repo.GetOrCreateActiveAsync(9, 15, It.IsAny<PaymentEntity>()))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 7,
                UserId = 9,
                EventId = 15,
                Amount = 2000,
                Status = PaymentStatus.Succeeded,
                ExternalPaymentIntentId = "pi_123"
            });

        var eventsService = new Mock<IEventsService>();
        eventsService.Setup(service => service.EnsureCanViewEventAsync(15, 9, "Participant"))
            .Returns(Task.CompletedTask);
        eventsService.Setup(service => service.GetEvent(15))
            .ReturnsAsync(new backend.main.features.events.Events
            {
                Id = 15,
                Name = "Paid Event",
                Description = "Charge",
                Location = "Campus",
                StartTime = DateTime.UtcNow.AddDays(1),
                ClubId = 4,
                registerCost = 2000
            });

        var service = CreateService(repository: repository, eventsService: eventsService);

        var act = () => service.CreatePaymentSession(9, "Participant", 15);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("You have already paid for this event.");
    }

    [Fact]
    public async Task RefundPayment_ShouldRejectNonSucceededPayments()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(repo => repo.GetByIdAsync(7))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 7,
                UserId = 9,
                EventId = 15,
                Amount = 2000,
                Status = PaymentStatus.Pending
            });

        var service = CreateService(repository: repository);

        var act = () => service.RefundPayment(7, 9);

        await act.Should().ThrowAsync<BadRequestException>()
            .WithMessage("Only succeeded payments can be refunded.");
    }

    [Fact]
    public async Task RefundPayment_ShouldRejectAnotherUsersPayment()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(repo => repo.GetByIdAsync(7))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 7,
                UserId = 33,
                EventId = 15,
                Amount = 2000,
                Status = PaymentStatus.Succeeded,
                ExternalPaymentIntentId = "pi_123"
            });

        var service = CreateService(repository: repository);

        var act = () => service.RefundPayment(7, 9);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("Not allowed.");
    }

    private static StripePaymentService CreateService(
        Mock<IPaymentRepository>? repository = null,
        Mock<IEventsService>? eventsService = null)
    {
        Environment.SetEnvironmentVariable("STRIPE_API_KEY", "sk_test");
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", "whsec_test");

        repository ??= new Mock<IPaymentRepository>();
        eventsService ??= new Mock<IEventsService>();

        return new StripePaymentService(repository.Object, eventsService.Object);
    }
}
