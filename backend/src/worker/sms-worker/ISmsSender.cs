using backend.main.shared.providers.messages;

namespace backend.worker.sms_worker;

public interface ISmsSender
{
    Task SendAsync(SmsMfaMessage message, CancellationToken cancellationToken = default);
}
