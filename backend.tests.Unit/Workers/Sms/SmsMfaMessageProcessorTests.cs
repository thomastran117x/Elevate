using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.worker.sms_worker;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Workers.Sms;

public class SmsMfaMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldSendSmsForValidPayload()
    {
        var sender = new Mock<ISmsSender>();
        var dlq = new Mock<ISmsWorkerDlqPublisher>();
        var processor = new SmsMfaMessageProcessor(sender.Object, dlq.Object);
        var envelope = CreateEnvelope(new SmsMfaMessage
        {
            PhoneNumber = "+14165550123",
            Code = "123456",
            Challenge = "challenge",
            Purpose = "mfa",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        });

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(
            It.Is<SmsMfaMessage>(message => message.PhoneNumber == "+14165550123"),
            It.IsAny<CancellationToken>()), Times.Once);
        dlq.Verify(service => service.PublishAsync(It.IsAny<KafkaMessageEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPublishDlqForMalformedPayload()
    {
        var sender = new Mock<ISmsSender>();
        var dlq = new Mock<ISmsWorkerDlqPublisher>();
        var processor = new SmsMfaMessageProcessor(sender.Object, dlq.Object);
        var envelope = new KafkaMessageEnvelope("eventxperience-sms", 0, 1, null, "{not-json}", new Dictionary<string, string?>());

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        dlq.Verify(service => service.PublishAsync(envelope, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRetryTransientFailuresBeforePublishingDlq()
    {
        var sender = new Mock<ISmsSender>();
        sender.Setup(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientSmsDeliveryException("twilio down"));
        var dlq = new Mock<ISmsWorkerDlqPublisher>();
        var processor = new SmsMfaMessageProcessor(sender.Object, dlq.Object);
        var envelope = CreateEnvelope(new SmsMfaMessage
        {
            PhoneNumber = "+14165550123",
            Code = "123456",
            Challenge = "challenge",
            Purpose = "mfa",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        });

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        dlq.Verify(service => service.PublishAsync(envelope, It.Is<string>(error => error.Contains("twilio down")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldNotRetryNonTransientFailures()
    {
        var sender = new Mock<ISmsSender>();
        sender.Setup(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad payload"));
        var dlq = new Mock<ISmsWorkerDlqPublisher>();
        var processor = new SmsMfaMessageProcessor(sender.Object, dlq.Object);

        await processor.ProcessAsync(CreateEnvelope(new SmsMfaMessage
        {
            PhoneNumber = "+14165550123",
            Code = "123456",
            Challenge = "challenge",
            Purpose = "mfa",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        }));

        sender.Verify(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        dlq.Verify(service => service.PublishAsync(It.IsAny<KafkaMessageEnvelope>(), It.Is<string>(error => error.Contains("bad payload")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldSwallowDlqPublishFailures()
    {
        var sender = new Mock<ISmsSender>();
        sender.Setup(service => service.SendAsync(It.IsAny<SmsMfaMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad payload"));
        var dlq = new Mock<ISmsWorkerDlqPublisher>();
        dlq.Setup(service => service.PublishAsync(It.IsAny<KafkaMessageEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("dlq unavailable"));
        var processor = new SmsMfaMessageProcessor(sender.Object, dlq.Object);

        var act = () => processor.ProcessAsync(CreateEnvelope(new SmsMfaMessage
        {
            PhoneNumber = "+14165550123",
            Code = "123456",
            Challenge = "challenge",
            Purpose = "mfa",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        }));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void SmsWorkerOptions_IsConfigured_ShouldRequireCredentialsAndSender()
    {
        var configured = new SmsWorkerOptions("kafka", "sms", "group", "dlq", "sid", "token", "mg-service", null);
        var missingCredentials = new SmsWorkerOptions("kafka", "sms", "group", "dlq", null, "token", "mg-service", null);
        var missingSender = new SmsWorkerOptions("kafka", "sms", "group", "dlq", "sid", "token", null, null);

        configured.IsConfigured.Should().BeTrue();
        missingCredentials.IsConfigured.Should().BeFalse();
        missingSender.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void EmailWorkerOptions_IsConfigured_ShouldMatchCurrentSmtpBehavior()
    {
        var configured = new backend.worker.email_worker.EmailWorkerOptions("kafka", "email", "group", "dlq", "status", "smtp", 587, "user", "pass", "http://localhost");
        var missingServer = new backend.worker.email_worker.EmailWorkerOptions("kafka", "email", "group", "dlq", "status", null, 587, "user", "pass", "http://localhost");

        configured.IsConfigured.Should().BeTrue();
        missingServer.IsConfigured.Should().BeFalse();
    }

    private static KafkaMessageEnvelope CreateEnvelope(SmsMfaMessage message)
    {
        return new KafkaMessageEnvelope(
            "eventxperience-sms",
            0,
            1,
            null,
            JsonSerializer.Serialize(message, JsonOptions.Default),
            new Dictionary<string, string?>());
    }
}
