using backend.main.shared.providers.messages;

namespace backend.worker.email_worker;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
