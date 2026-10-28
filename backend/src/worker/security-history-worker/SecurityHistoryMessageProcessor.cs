using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.main.shared.utilities.logger;

namespace backend.worker.security_history_worker;

/// <summary>
/// Turns one security history message into a stored entry. Any alert it owes is stored with it and
/// sent by <see cref="SecurityHistoryAlertService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at least once and replay is safe: the unique <c>MessageId</c> turns a redelivery
/// into <see cref="SecurityHistoryWriteResult.Duplicate"/>, and because the alert's state lives on
/// the row it was stored with, a redelivery neither sends a second alert nor loses one.
/// </para>
/// <para>
/// Only a payload that can never become an entry goes to the DLQ. A storage failure is left to
/// propagate after the execution strategy's own retries: the offset stays uncommitted and the
/// consumer processes the message again once the database answers, instead of dead-lettering every
/// message that arrives during an outage.
/// </para>
/// </remarks>
public sealed class SecurityHistoryMessageProcessor
{
    private readonly SecurityHistoryWriter _writer;
    private readonly ISecurityHistoryWorkerDlqPublisher _dlqPublisher;
    private readonly SecurityHistoryWorkerMetrics _metrics;

    public SecurityHistoryMessageProcessor(
        SecurityHistoryWriter writer,
        ISecurityHistoryWorkerDlqPublisher dlqPublisher,
        SecurityHistoryWorkerMetrics metrics)
    {
        _writer = writer;
        _dlqPublisher = dlqPublisher;
        _metrics = metrics;
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
            result = await _writer.InsertAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The type only: a database error can quote the row it was handed.
            _metrics.RecordEntry(SecurityHistoryEntryOutcome.Failed);
            Logger.Warn($"[SecurityHistoryMessageProcessor] Storing entry {message.MessageId} failed with {ex.GetType().Name}; it will be retried.");
            throw;
        }

        switch (result)
        {
            case SecurityHistoryWriteResult.Inserted:
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
