using backend.main.shared.providers.messages;

namespace backend.tests.Integration.Infrastructure;

public sealed class KafkaBackedPublisher
{
    private readonly object _gate = new();
    private readonly AuthApiTestApp _app;
    private List<EmailMessage> _emailMessages = [];
    private List<SmsMfaMessage> _smsMessages = [];

    public KafkaBackedPublisher(AuthApiTestApp app)
    {
        _app = app;
    }

    public IReadOnlyList<EmailMessage> EmailMessages
    {
        get
        {
            lock (_gate)
            {
                _emailMessages.AddRange(
                    _app.ReadNewEmailMessagesAsync(TimeSpan.FromMilliseconds(750))
                        .GetAwaiter()
                        .GetResult());
                return _emailMessages.ToArray();
            }
        }
    }

    public IReadOnlyList<SmsMfaMessage> SmsMessages
    {
        get
        {
            lock (_gate)
            {
                _smsMessages.AddRange(
                    _app.ReadNewSmsMessagesAsync(TimeSpan.FromMilliseconds(750))
                        .GetAwaiter()
                        .GetResult());
                return _smsMessages.ToArray();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _emailMessages.Clear();
            _smsMessages.Clear();
            _app.MarkNotificationBoundaryAsync().GetAwaiter().GetResult();
        }
    }
}
