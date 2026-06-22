using System.Net;
using System.Net.Http.Headers;
using System.Text;

using backend.main.shared.providers.messages;
using backend.worker.sms_worker;

using FluentAssertions;

namespace backend.tests.Unit.Workers.Sms;

public class TwilioSmsSenderTests
{
    [Fact]
    public async Task SendAsync_ShouldUseMessagingServiceAndExcludeChallengeFromBody()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var client = new HttpClient(handler);
        var sender = new TwilioSmsSender(
            client,
            new SmsWorkerOptions("kafka", "sms", "group", "dlq", "sid", "token", "mg-service", null)
        );

        await sender.SendAsync(CreateMessage());

        handler.Requests.Should().ContainSingle();
        handler.AuthorizationHeader.Should().BeEquivalentTo(new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("sid:token"))
        ));
        handler.LastBody.Should().Contain("MessagingServiceSid=mg-service");
        handler.LastBody.Should().Contain("123456");
        handler.LastBody.Should().NotContain("challenge-123");
    }

    [Fact]
    public async Task SendAsync_ShouldThrowTransientExceptionForRateLimitResponses()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("slow down")
        });
        using var client = new HttpClient(handler);
        var sender = new TwilioSmsSender(
            client,
            new SmsWorkerOptions("kafka", "sms", "group", "dlq", "sid", "token", null, "+14165550123")
        );

        var act = () => sender.SendAsync(CreateMessage());

        await act.Should().ThrowAsync<TransientSmsDeliveryException>()
            .WithMessage("*429*");
    }

    [Fact]
    public async Task SendAsync_ShouldThrowInvalidOperationForPermanentFailures()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid destination")
        });
        using var client = new HttpClient(handler);
        var sender = new TwilioSmsSender(
            client,
            new SmsWorkerOptions("kafka", "sms", "group", "dlq", "sid", "token", null, "+14165550123")
        );

        var act = () => sender.SendAsync(CreateMessage());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*400*");
    }

    private static SmsMfaMessage CreateMessage()
    {
        return new SmsMfaMessage
        {
            PhoneNumber = "+14165550123",
            Code = "123456",
            Challenge = "challenge-123",
            Purpose = "mfa",
            ExpiresAtUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = new();
        public AuthenticationHeaderValue? AuthorizationHeader { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            AuthorizationHeader = request.Headers.Authorization;
            LastBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
