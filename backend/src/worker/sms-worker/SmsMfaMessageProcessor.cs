using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.main.shared.utilities.logger;

using Polly;
using Polly.Retry;

namespace backend.worker.sms_worker;

public sealed class SmsMfaMessageProcessor
{
    private readonly ISmsSender _smsSender;
    private readonly ISmsWorkerDlqPublisher _dlqPublisher;

    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>()
        })
        .Build();

    public SmsMfaMessageProcessor(
        ISmsSender smsSender,
        ISmsWorkerDlqPublisher dlqPublisher)
    {
        _smsSender = smsSender;
        _dlqPublisher = dlqPublisher;
    }

    public async Task ProcessAsync(
        KafkaMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        SmsMfaMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<SmsMfaMessage>(envelope.Payload, JsonOptions.Default)
                ?? throw new InvalidOperationException("SMS payload could not be deserialized.");

            await RetryPipeline.ExecuteAsync(
                async (CancellationToken ct) => await _smsSender.SendAsync(message, ct),
                cancellationToken
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            Logger.Warn(ex, "Malformed SMS payload. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            var destination = message?.PhoneNumber ?? "unknown";
            Logger.Warn(ex, $"SMS delivery failed for '{destination}'. Publishing to Kafka DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
        }
    }
}
