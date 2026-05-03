using backend.main.dtos;

using FluentAssertions;

using Xunit;

namespace backend.test.Workers;

public class EmailMessageProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ValidMessage_SendsEmail()
    {
        var sender = new FakeEmailSender();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EmailMessageProcessor(sender, dlq);

        await processor.ProcessAsync(CreateEnvelope(
            """
            {
              "type": "VerifyEmail",
              "email": "user@example.com",
              "token": "verify-token",
              "code": "123456"
            }
            """
        ));

        sender.Messages.Should().ContainSingle();
        dlq.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_MalformedPayload_PublishesToDlq()
    {
        var sender = new FakeEmailSender();
        var dlq = new FakeDlqPublisher();
        var processor = new backend.worker.event_indexer.EmailMessageProcessor(sender, dlq);

        await processor.ProcessAsync(CreateEnvelope("""{ "type": "VerifyEmail" """));

        sender.Messages.Should().BeEmpty();
        dlq.Messages.Should().ContainSingle();
    }

    private static backend.worker.event_indexer.EventIndexerEnvelope CreateEnvelope(string payload) =>
        new(
            "eventxperience-email",
            0,
            10,
            "email-1",
            payload,
            null,
            new Dictionary<string, string?>()
        );

    private sealed class FakeEmailSender : backend.worker.event_indexer.IEmailSender
    {
        public List<EmailMessage> Messages { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDlqPublisher : backend.worker.event_indexer.IEmailWorkerDlqPublisher
    {
        public List<(backend.worker.event_indexer.EventIndexerEnvelope Envelope, string Error)> Messages { get; } = new();

        public Task PublishAsync(
            backend.worker.event_indexer.EventIndexerEnvelope envelope,
            string error,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((envelope, error));
            return Task.CompletedTask;
        }
    }
}
