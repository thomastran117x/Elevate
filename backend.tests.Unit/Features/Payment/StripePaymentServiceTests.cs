using System.Security.Cryptography;
using System.Text;

using backend.main.features.events;
using backend.main.features.payment;
using backend.main.shared.exceptions.http;

using FluentAssertions;

using backend.tests.Unit.Support;

using Moq;

using Stripe;

using PaymentEntity = backend.main.features.payment.Payment;

namespace backend.tests.Unit.Features.Payment;

[Collection(EnvironmentVariableTestCollection.Name)]
public class StripePaymentServiceTests
{
    private const string WebhookSecret = "whsec_test";


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
                registerCost = 0,
                LifecycleState = EventLifecycleState.Published
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
                registerCost = 2000,
                LifecycleState = EventLifecycleState.Published
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

    [Theory]
    [InlineData(EventLifecycleState.Paused)]
    [InlineData(EventLifecycleState.Draft)]
    [InlineData(EventLifecycleState.Cancelled)]
    [InlineData(EventLifecycleState.Archived)]
    public async Task CreatePaymentSession_ShouldRefuseAnyEventThatIsNotOpenForRegistration(
        EventLifecycleState lifecycleState)
    {
        var repository = new Mock<IPaymentRepository>();
        var eventsService = new Mock<IEventsService>();
        eventsService.Setup(service => service.EnsureCanViewEventAsync(15, 9, "Participant"))
            .Returns(Task.CompletedTask);
        eventsService.Setup(service => service.GetEvent(15))
            .ReturnsAsync(PayableEvent(lifecycleState));

        var service = CreateService(repository: repository, eventsService: eventsService);

        var act = () => service.CreatePaymentSession(9, "Participant", 15);

        // Paused and cancelled events keep their detail page so people who already signed up do
        // not lose it, so "can view" no longer implies "can register". Without this check the
        // checkout path would take money for a registration the product says is closed.
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Registration is only available for published events.");

        repository.Verify(
            repo => repo.GetOrCreateActiveAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<PaymentEntity>()),
            Times.Never);
    }

    [Fact]
    public async Task CreatePaymentSession_ShouldStillHonourIdempotencyForAnEventPausedAfterTheAttempt()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(repo => repo.GetByIdempotencyKeyAsync("idem-paused"))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 21,
                UserId = 9,
                EventId = 15,
                Amount = 2000,
                Status = PaymentStatus.Pending,
                CheckoutUrl = "https://checkout.test/existing"
            });

        var eventsService = new Mock<IEventsService>();
        eventsService.Setup(service => service.GetEvent(15))
            .ReturnsAsync(PayableEvent(EventLifecycleState.Paused));

        var service = CreateService(repository: repository, eventsService: eventsService);

        var result = await service.CreatePaymentSession(9, "Participant", 15, "idem-paused");

        // A retry of a request made before the pause is the same logical attempt, not a new one,
        // so it gets its existing payment back rather than an error. The registration check sits
        // after the idempotency return on purpose.
        result.Id.Should().Be(21);
        eventsService.Verify(
            service => service.EnsureCanViewEventAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Records current behaviour rather than asserting it is ideal: a checkout session created
    /// before an event was paused can still be completed on Stripe's side, and the webhook marks
    /// it Succeeded regardless of lifecycle state.
    /// <para>
    /// That is deliberate as far as the money goes — the payment happened, so it must be
    /// recorded — and it does not grant a place, because registration is gated separately. The
    /// open question is whether pausing should also expire outstanding sessions; this test exists
    /// so that choice is visible if the behaviour ever changes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task HandleWebhook_ShouldRecordACompletionForAPausedEvent_WithoutGrantingAPlace()
    {
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(repo => repo.GetByExternalSessionIdAsync("sess_paused"))
            .ReturnsAsync(new PaymentEntity
            {
                Id = 31,
                UserId = 9,
                EventId = 15,
                Status = PaymentStatus.Pending,
                ExternalSessionId = "sess_paused"
            });

        var eventsService = new Mock<IEventsService>();
        var service = CreateService(repository: repository, eventsService: eventsService);

        var payload = CheckoutCompletedPayload("sess_paused");

        await service.HandleWebhook(payload, SignPayload(payload));

        repository.Verify(
            repo => repo.UpdateStatusAsync(31, PaymentStatus.Succeeded, It.IsAny<string?>()),
            Times.Once);

        // The webhook never registers anyone; that stays gated on AllowsRegistration elsewhere.
        eventsService.Verify(
            service => service.GetEvent(It.IsAny<int>()),
            Times.Never);
    }

    private static backend.main.features.events.Events PayableEvent(
        EventLifecycleState lifecycleState) => new()
        {
            Id = 15,
            Name = "Paid Event",
            Description = "Costs money",
            Location = "Campus",
            StartTime = DateTime.UtcNow.AddDays(1),
            ClubId = 4,
            registerCost = 2000,
            LifecycleState = lifecycleState
        };

    /// <summary>
    /// A minimal <c>checkout.session.completed</c> event. The API version is read from the
    /// library rather than hardcoded, so a Stripe.net bump cannot silently invalidate it.
    /// </summary>
    private static string CheckoutCompletedPayload(string sessionId) =>
        $$"""
        {
          "id": "evt_test",
          "object": "event",
          "api_version": "{{StripeConfiguration.ApiVersion}}",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "payment_intent": "pi_test"
            }
          }
        }
        """;

    /// <summary>Builds the <c>Stripe-Signature</c> header the SDK verifies against.</summary>
    private static string SignPayload(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static StripePaymentService CreateService(
        Mock<IPaymentRepository>? repository = null,
        Mock<IEventsService>? eventsService = null)
    {
        Environment.SetEnvironmentVariable("STRIPE_API_KEY", "sk_test");
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", WebhookSecret);

        repository ??= new Mock<IPaymentRepository>();
        eventsService ??= new Mock<IEventsService>();

        return new StripePaymentService(repository.Object, eventsService.Object);
    }
}
