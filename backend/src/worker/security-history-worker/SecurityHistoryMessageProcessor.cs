using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.main.shared.utilities.logger;

using Polly;
using Polly.Retry;

namespace backend.worker.security_history_worker;

/// <summary>
/// Turns one security history message into a stored entry and, when due, an alert.
/// </summary>
/// <remarks>
/// Delivery is at least once and replay is safe: the unique <c>MessageId</c> turns a redelivery
/// into <see cref="SecurityHistoryWriteResult.Duplicate"/>, and only a newly inserted entry can
/// alert, so a replay never emails twice. A malformed payload, or an insert that still fails after
/// the retries, goes to the DLQ so the queue keeps moving.
/// </remarks>
public sealed class SecurityHistoryMessageProcessor
{
    private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => ex is not OperationCanceledException)
        })
        .Build();

    private readonly SecurityHistoryWriter _writer;
    private readonly SecurityHistoryAlertSender _alerts;
    private readonly ISecurityHistoryWorkerDlqPublisher _dlqPublisher;
    private readonly SecurityHistoryWorkerMetrics _metrics;
    private readonly ResiliencePipeline _retryPipeline;

    public SecurityHistoryMessageProcessor(
        SecurityHistoryWriter writer,
        SecurityHistoryAlertSender alerts,
        ISecurityHistoryWorkerDlqPublisher dlqPublisher,
        SecurityHistoryWorkerMetrics metrics)
        : this(writer, alerts, dlqPublisher, metrics, RetryPipeline)
    {
    }

    /// <summary>Lets tests run the retries without waiting out the backoff.</summary>
    internal SecurityHistoryMessageProcessor(
        SecurityHistoryWriter writer,
        SecurityHistoryAlertSender alerts,
        ISecurityHistoryWorkerDlqPublisher dlqPublisher,
        SecurityHistoryWorkerMetrics metrics,
        ResiliencePipeline retryPipeline)
    {
        _writer = writer;
        _alerts = alerts;
        _dlqPublisher = dlqPublisher;
        _metrics = metrics;
        _retryPipeline = retryPipeline;
    }

    public async Task<SecurityHistoryEntryOutcome> ProcessAsync(
        KafkaMessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        SecurityHistoryMessage message;
        try
        {
            message = SecurityHistoryMessageParser.Parse(envelope.Payload);
        }
        catch (InvalidSecurityHistoryMessageException ex)
        {
            Logger.Warn($"[SecurityHistoryMessageProcessor] Rejected message at offset {envelope.Offset}: {ex.Message} Publishing to the DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.Message, cancellationToken);
            return Record(SecurityHistoryEntryOutcome.Rejected);
        }

        SecurityHistoryWriteResult result;
        try
        {
            result = await _retryPipeline.ExecuteAsync(
                async ct => await _writer.InsertAsync(message, ct),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type only: a database error can quote the row it was handed.
            Logger.Warn($"[SecurityHistoryMessageProcessor] Storing entry {message.MessageId} failed with {ex.GetType().Name}. Publishing to the DLQ.");
            await _dlqPublisher.PublishAsync(envelope, ex.GetType().Name, cancellationToken);
            return Record(SecurityHistoryEntryOutcome.Failed);
        }

        switch (result)
        {
            case SecurityHistoryWriteResult.Inserted:
                await _alerts.SendIfDueAsync(message, cancellationToken);
                return Record(SecurityHistoryEntryOutcome.Inserted);
            case SecurityHistoryWriteResult.Duplicate:
                return Record(SecurityHistoryEntryOutcome.Duplicate);
            case SecurityHistoryWriteResult.AccountDeleted:
                Logger.Info($"[SecurityHistoryMessageProcessor] Dropped entry {message.MessageId}: the account was deleted.");
                return Record(SecurityHistoryEntryOutcome.AccountDeleted);
            default:
                return Record(SecurityHistoryEntryOutcome.Expired);
        }
    }

    private SecurityHistoryEntryOutcome Record(SecurityHistoryEntryOutcome outcome)
    {
        _metrics.RecordEntry(outcome);
        return outcome;
    }
}
