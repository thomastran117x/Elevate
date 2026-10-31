using System.Threading.Channels;

using backend.main.shared.providers.messages;

namespace backend.main.features.auth.history;

/// <summary>
/// Hands history entries from the request thread to <see cref="SecurityHistoryOutboxService"/>.
/// </summary>
/// <remarks>
/// Sinks run inline and must not block, so the request only enqueues; the outbox write happens on
/// a background thread a moment later. Bounded so a database outage cannot grow memory without
/// limit: while the writer cannot keep up, an entry that does not fit is refused and counted.
/// </remarks>
public sealed class SecurityHistoryQueue
{
    public const int Capacity = 10_000;

    private readonly Channel<SecurityHistoryMessage> _channel =
        Channel.CreateBounded<SecurityHistoryMessage>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <returns>False when the queue is full and the entry was not accepted.</returns>
    public bool TryEnqueue(SecurityHistoryMessage message) => _channel.Writer.TryWrite(message);

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);

    /// <summary>Takes up to <paramref name="max"/> queued entries without waiting.</summary>
    public IReadOnlyList<SecurityHistoryMessage> TakeBatch(int max)
    {
        var batch = new List<SecurityHistoryMessage>(Math.Min(max, 64));
        while (batch.Count < max && _channel.Reader.TryRead(out var message))
            batch.Add(message);
        return batch;
    }
}
