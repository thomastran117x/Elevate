using backend.main.shared.providers.messages;
using backend.worker.email_worker;

namespace backend.tests.Unit.Workers.Email;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_ShouldThrow_WhenSmtpConfigurationIsMissing()
    {
        var sender = CreateSender(CreateOptions());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(new EmailMessage
            {
                Email = "member@example.com",
                Token = "verify-token",
                Type = EmailMessageType.VerifyEmail,
                Code = "123456"
            }));

        Assert.Equal("Email worker is missing SMTP configuration.", exception.Message);
    }

    [Fact]
    public async Task SendAsync_ShouldRejectInvalidRecipientBeforeConnecting()
    {
        var sender = CreateSender(CreateOptions(
            smtpServer: "smtp.example.com",
            smtpPort: 587,
            username: "noreply@example.com",
            password: "secret"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(new EmailMessage
            {
                Email = "not-an-email",
                Token = "verify-token",
                Type = EmailMessageType.VerifyEmail,
                Code = "123456"
            }));

        Assert.Equal("Email payload contains an invalid recipient address.", exception.Message);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    private static SmtpEmailSender CreateSender(EmailWorkerOptions options) =>
        new(options, new EmailTemplateRenderer(options));

    private static EmailWorkerOptions CreateOptions(
        string bootstrapServers = "kafka:9092",
        string topic = "eventxperience-email",
        string groupId = "email-worker-group",
        string dlqTopic = "eventxperience-email-dlq",
        string statusTopic = "eventxperience-email-status",
        string? smtpServer = null,
        int smtpPort = 587,
        string? username = null,
        string? password = null,
        string frontendBaseUrl = "https://frontend.example.com/") =>
        new(
            bootstrapServers,
            topic,
            groupId,
            dlqTopic,
            statusTopic,
            smtpServer,
            smtpPort,
            username,
            password,
            frontendBaseUrl);
}
