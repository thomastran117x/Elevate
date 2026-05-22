using backend.main.shared.providers;
using backend.main.shared.providers.messages;

namespace backend.tests.Integration.Infrastructure;

public sealed class CapturingPublisher : IPublisher
{
    private readonly object _gate = new();
    private readonly List<(string Topic, object Message)> _messages = [];

    public Task PublishAsync<T>(string topic, T message)
    {
        lock (_gate)
        {
            _messages.Add((topic, message!));
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> EmailMessages
    {
        get
        {
            lock (_gate)
            {
                return _messages
                    .Select(entry => entry.Message)
                    .OfType<EmailMessage>()
                    .ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }
}
