using System.Collections.Concurrent;
using System.Threading.Channels;

namespace backend.main.features.clubs.posts.comments
{
    public record CommentEvent(string Type, object Payload);

    public class CommentEventBroker
    {
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, ChannelWriter<CommentEvent>>> _streams = new();

        public void Subscribe(int postId, Guid id, ChannelWriter<CommentEvent> writer) =>
            _streams.GetOrAdd(postId, _ => new()).TryAdd(id, writer);

        public void Unsubscribe(int postId, Guid id)
        {
            if (_streams.TryGetValue(postId, out var subs))
            {
                subs.TryRemove(id, out _);
                if (subs.IsEmpty)
                    _streams.TryRemove(postId, out _);
            }
        }

        public void Publish(int postId, CommentEvent evt)
        {
            if (!_streams.TryGetValue(postId, out var subs))
                return;
            foreach (var (_, writer) in subs)
                writer.TryWrite(evt);
        }
    }
}
