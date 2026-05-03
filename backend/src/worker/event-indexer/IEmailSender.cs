using backend.main.dtos;

namespace backend.worker.event_indexer;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
