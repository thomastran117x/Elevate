using System.Net;
using System.Net.Http.Headers;

using backend.main.features.payment;
using backend.main.features.payment.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PaymentEntity = backend.main.features.payment.Payment;

namespace backend.tests.Integration.Features.Payment;

public class PaymentEndpointsTests
{
    [Fact]
    public async Task PaymentEndpoints_ShouldSupportCreateGetMineRefundAndWebhook()
    {
        var fakePaymentService = new FakePaymentService();
        await using var app = await AuthApiTestApp.CreateAsync(services =>
        {
            services.RemoveAll<IPaymentService>();
            services.AddSingleton<IPaymentService>(fakePaymentService);
        });

        var owner = await app.SeedUserAsync("payment-owner@example.com", role: "Participant");
        await app.SeedKnownDeviceAsync(owner.Id, "payment-owner-device");
        var ownerSession = await app.LoginApiAsync("payment-owner@example.com", trustedDeviceToken: "payment-owner-device");

        var other = await app.SeedUserAsync("payment-other@example.com", role: "Participant");

        fakePaymentService.PaymentsById[41] = new PaymentEntity
        {
            Id = 41,
            UserId = owner.Id,
            EventId = 15,
            Amount = 2500,
            Currency = "usd",
            Status = PaymentStatus.Pending,
            CheckoutUrl = "https://checkout.test/41"
        };
        fakePaymentService.PaymentsById[99] = new PaymentEntity
        {
            Id = 99,
            UserId = other.Id,
            EventId = 19,
            Amount = 3000,
            Currency = "usd",
            Status = PaymentStatus.Succeeded,
            CheckoutUrl = "https://checkout.test/99",
            ExternalPaymentIntentId = "pi_99"
        };

        fakePaymentService.PaymentsByUser[owner.Id] =
        [
            fakePaymentService.PaymentsById[41]
        ];

        var create = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/payments/15",
            ownerSession.AccessToken,
            idempotencyKey: "idem-pay-1"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var createBody = await app.ReadApiResponseAsync<PaymentResponse>(create);
        createBody.Data!.CheckoutUrl.Should().Be("https://checkout.test/created-15");
        fakePaymentService.CreatedSessions.Should().ContainSingle();
        fakePaymentService.CreatedSessions[0].IdempotencyKey.Should().Be("idem-pay-1");

        var getMine = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/payments/41",
            ownerSession.AccessToken));
        getMine.StatusCode.Should().Be(HttpStatusCode.OK);

        var forbidden = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/payments/99",
            ownerSession.AccessToken));
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var mine = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/payments/me?page=1&pageSize=20",
            ownerSession.AccessToken));
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        var mineBody = await app.ReadApiResponseAsync<IEnumerable<PaymentResponse>>(mine);
        mineBody.Data.Should().ContainSingle(payment => payment.Id == 41);

        var refund = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/payments/41/refund",
            ownerSession.AccessToken));
        refund.StatusCode.Should().Be(HttpStatusCode.OK);
        fakePaymentService.RefundRequests.Should().ContainSingle(request => request.PaymentId == 41 && request.RequestingUserId == owner.Id);

        var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook")
        {
            Content = new StringContent("{\"id\":\"evt_test\"}")
        };
        webhook.Headers.Add("Stripe-Signature", "sig_test");

        var webhookResponse = await app.Client.SendAsync(webhook);
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        fakePaymentService.WebhookPayloads.Should().ContainSingle();
        fakePaymentService.WebhookPayloads[0].Payload.Should().Be("{\"id\":\"evt_test\"}");
        fakePaymentService.WebhookPayloads[0].Signature.Should().Be("sig_test");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey != null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private sealed class FakePaymentService : IPaymentService
    {
        public Dictionary<int, PaymentEntity> PaymentsById { get; } = [];
        public Dictionary<int, List<PaymentEntity>> PaymentsByUser { get; } = [];
        public List<(int UserId, string UserRole, int EventId, string? IdempotencyKey)> CreatedSessions { get; } = [];
        public List<(string Payload, string Signature)> WebhookPayloads { get; } = [];
        public List<(int PaymentId, int RequestingUserId)> RefundRequests { get; } = [];

        public Task<PaymentEntity> CreatePaymentSession(int userId, string userRole, int eventId, string? idempotencyKey = null)
        {
            CreatedSessions.Add((userId, userRole, eventId, idempotencyKey));
            return Task.FromResult(new PaymentEntity
            {
                Id = 500 + eventId,
                UserId = userId,
                EventId = eventId,
                Amount = 2500,
                Currency = "usd",
                Status = PaymentStatus.Pending,
                CheckoutUrl = $"https://checkout.test/created-{eventId}"
            });
        }

        public Task<PaymentEntity> GetPayment(int paymentId)
        {
            if (!PaymentsById.TryGetValue(paymentId, out var payment))
                throw new ResourceNotFoundException($"Payment {paymentId} not found.");

            return Task.FromResult(payment);
        }

        public Task<List<PaymentEntity>> GetPaymentsByUser(int userId, int page = 1, int pageSize = 20)
        {
            return Task.FromResult(PaymentsByUser.TryGetValue(userId, out var payments)
                ? payments
                : []);
        }

        public Task HandleWebhook(string payload, string signature)
        {
            WebhookPayloads.Add((payload, signature));
            return Task.CompletedTask;
        }

        public Task<PaymentEntity> RefundPayment(int paymentId, int requestingUserId)
        {
            RefundRequests.Add((paymentId, requestingUserId));

            if (!PaymentsById.TryGetValue(paymentId, out var payment))
                throw new ResourceNotFoundException($"Payment {paymentId} not found.");

            if (payment.UserId != requestingUserId)
                throw new ForbiddenException("Not allowed.");

            payment.Status = PaymentStatus.Refunded;
            return Task.FromResult(payment);
        }
    }
}
