using System.Text.Json;

using backend.main.features.events.invitations;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.worker.email_worker;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Workers.Email;

public class EmailMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ShouldSendEmailAndPublishSentStatus()
    {
        var sender = new Mock<IEmailSender>();
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);
        var envelope = CreateEnvelope(new EmailMessage
        {
            Type = EmailMessageType.VerifyEmail,
            Email = "member@example.com",
            Token = "verify-token",
            Code = "123456",
            EventInvitationId = 42
        });

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        status.Verify(service => service.PublishAsync(
            It.Is<EmailDeliveryStatusMessage>(message =>
                message.EventInvitationId == 42
                && message.DeliveryStatus == EventInvitationDeliveryStatus.Sent
                && message.ErrorMessage == null),
            It.IsAny<CancellationToken>()), Times.Once);
        dlq.Verify(service => service.PublishAsync(It.IsAny<KafkaMessageEnvelope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ShouldSkipStatusPublishWhenInvitationIdIsMissing()
    {
        var sender = new Mock<IEmailSender>();
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);

        await processor.ProcessAsync(CreateEnvelope(new EmailMessage
        {
            Type = EmailMessageType.VerifyEmail,
            Email = "member@example.com",
            Token = "verify-token",
            Code = "123456"
        }));

        status.Verify(service => service.PublishAsync(It.IsAny<EmailDeliveryStatusMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPublishDlqForMalformedPayload()
    {
        var sender = new Mock<IEmailSender>();
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);
        var envelope = new KafkaMessageEnvelope("eventxperience-email", 0, 1, null, "{not-json}", new Dictionary<string, string?>());

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        status.Verify(service => service.PublishAsync(It.IsAny<EmailDeliveryStatusMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        dlq.Verify(service => service.PublishAsync(envelope, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRetryFailuresThenPublishFailedStatusAndDlq()
    {
        var sender = new Mock<IEmailSender>();
        sender.Setup(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);
        var envelope = CreateEnvelope(new EmailMessage
        {
            Type = EmailMessageType.VerifyEmail,
            Email = "member@example.com",
            Token = "verify-token",
            Code = "123456",
            EventInvitationId = 42
        });

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        status.Verify(service => service.PublishAsync(
            It.Is<EmailDeliveryStatusMessage>(message =>
                message.EventInvitationId == 42
                && message.DeliveryStatus == EventInvitationDeliveryStatus.Failed
                && message.ErrorMessage == "smtp down"),
            It.IsAny<CancellationToken>()), Times.Once);
        dlq.Verify(service => service.PublishAsync(envelope, "smtp down", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPublishDlqWhenEmailIsMissing()
    {
        var sender = new Mock<IEmailSender>();
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);
        var envelope = CreateEnvelope(new EmailMessage
        {
            Type = EmailMessageType.VerifyEmail,
            Email = " ",
            Token = "verify-token",
            EventInvitationId = 42
        });

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        status.Verify(service => service.PublishAsync(
            It.Is<EmailDeliveryStatusMessage>(message =>
                message.EventInvitationId == 42
                && message.DeliveryStatus == EventInvitationDeliveryStatus.Failed
                && message.ErrorMessage!.Contains("recipient address")),
            It.IsAny<CancellationToken>()), Times.Once);
        dlq.Verify(service => service.PublishAsync(envelope, It.Is<string>(error => error.Contains("recipient address")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldPublishDlqWhenTokenIsMissing()
    {
        var sender = new Mock<IEmailSender>();
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);
        var envelope = CreateEnvelope(new EmailMessage
        {
            Type = EmailMessageType.VerifyEmail,
            Email = "member@example.com",
            Token = " ",
            EventInvitationId = 42
        });

        await processor.ProcessAsync(envelope);

        sender.Verify(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        status.Verify(service => service.PublishAsync(
            It.Is<EmailDeliveryStatusMessage>(message =>
                message.EventInvitationId == 42
                && message.DeliveryStatus == EventInvitationDeliveryStatus.Failed
                && message.ErrorMessage!.Contains("requires a token")),
            It.IsAny<CancellationToken>()), Times.Once);
        dlq.Verify(service => service.PublishAsync(envelope, It.Is<string>(error => error.Contains("requires a token")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ShouldRethrowWhenCancelled()
    {
        var sender = new Mock<IEmailSender>();
        sender.Setup(service => service.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var dlq = new Mock<IEmailWorkerDlqPublisher>();
        var status = new Mock<IEmailDeliveryStatusPublisher>();
        var processor = new EmailMessageProcessor(sender.Object, dlq.Object, status.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => processor.ProcessAsync(CreateEnvelope(new EmailMessage
        {
            Type = EmailMessageType.VerifyEmail,
            Email = "member@example.com",
            Token = "verify-token"
        }), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static KafkaMessageEnvelope CreateEnvelope(EmailMessage message)
    {
        return new KafkaMessageEnvelope(
            "eventxperience-email",
            0,
            1,
            null,
            JsonSerializer.Serialize(message, JsonOptions.Default),
            new Dictionary<string, string?>());
    }
}
