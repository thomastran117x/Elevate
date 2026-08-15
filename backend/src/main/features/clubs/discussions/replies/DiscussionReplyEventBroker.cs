using System.Collections.Concurrent;
using System.Threading.Channels;

namespace backend.main.features.clubs.discussions.replies;

public sealed record DiscussionReplyEvent(string Type, object Payload);

public sealed class DiscussionReplyEventBroker
{
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, ChannelWriter<DiscussionReplyEvent>>> _streams = new();

    public void Subscribe(int clubId, Guid id, ChannelWriter<DiscussionReplyEvent> writer) =>
        _streams.GetOrAdd(clubId, _ => new()).TryAdd(id, writer);

    public void Unsubscribe(int clubId, Guid id)
    {
        if (!_streams.TryGetValue(clubId, out var subscribers))
            return;
        subscribers.TryRemove(id, out _);
        if (subscribers.IsEmpty)
            _streams.TryRemove(clubId, out _);
    }

    public void Publish(int clubId, DiscussionReplyEvent evt)
    {
        if (!_streams.TryGetValue(clubId, out var subscribers))
            return;
        foreach (var writer in subscribers.Values)
            writer.TryWrite(evt);
    }
}
