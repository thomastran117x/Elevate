using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.main.shared.utilities.logger;

using Polly;
using Polly.Retry;

namespace backend.worker.email_worker;

public sealed class EmailMessageProcessor
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailWorkerDlqPublisher _dlqPublisher;
    private readonly IEmailDeliveryStatusPublisher _statusPublisher;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>()
        })
        .Build();

    public EmailMessageProcessor(
        IEmailSender emailSender,
        IEmailWorkerDlqPublisher dlqPublisher,
        IEmailDeliveryStatusPublisher statusPublisher)
    {
        _emailSender = emailSender;
        _dlqPublisher = dlqPublisher;
        _statusPublisher = statusPublisher;
    }

    public async Task ProcessAsync(
        KafkaMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        EmailMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<EmailMessage>(envelope.Payload, JsonOptions.Default)
                ?? throw new InvalidOperationException("Email payload could not be deserialized.");

            ValidateMessage(message);

            await RetryPipeline.ExecuteAsync(
                async (CancellationToken ct) => await _emailSender.SendAsync(message, ct),
                cancellationToken
            );

            await PublishStatusAsync(message, backend.main.features.events.invitations.EventInvitationDeliveryStatus.Sent, null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Malformed email payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var destination = message?.Email ?? "unknown";
            Logger.Warn(ex, $"Email delivery failed for '{destination}'. Publishing to Kafka DLQ.");
            if (message != null)
            {
                await PublishStatusAsync(
                    message,
                    backend.main.features.events.invitations.EventInvitationDeliveryStatus.Failed,
                    ex.Message,
                    cancellationToken);
            }
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }

    private async Task PublishStatusAsync(
        EmailMessage message,
        backend.main.features.events.invitations.EventInvitationDeliveryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        if (message.EventInvitationId == null)
            return;

        await _statusPublisher.PublishAsync(new EmailDeliveryStatusMessage
        {
            Type = message.Type,
            EventInvitationId = message.EventInvitationId,
            DeliveryStatus = status,
            ErrorMessage = errorMessage
        }, cancellationToken);
    }

    private static void ValidateMessage(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Email))
            throw new InvalidOperationException("Email payload requires a recipient address.");

        // Token requirement is per-type and enforced by the content renderer, since
        // several email types (welcome, password-changed, reminders) carry no token.
    }
}
