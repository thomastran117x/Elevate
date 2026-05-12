using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.utilities.logger;

using Polly;
using Polly.Retry;

namespace backend.worker.email_worker;

public sealed class EmailMessageProcessor
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailWorkerDlqPublisher _dlqPublisher;

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
        IEmailWorkerDlqPublisher dlqPublisher)
    {
        _emailSender = emailSender;
        _dlqPublisher = dlqPublisher;
    }

    public async Task ProcessAsync(
        EmailWorkerEnvelope envelope,
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
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }

    private static void ValidateMessage(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Email))
            throw new InvalidOperationException("Email payload requires a recipient address.");

        if (string.IsNullOrWhiteSpace(message.Token))
            throw new InvalidOperationException("Email payload requires a token.");
    }
}
