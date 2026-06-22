using System.Reflection;

using backend.main.shared.providers.messages;
using backend.worker.email_worker;

namespace backend.tests.Unit.Workers.Email;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_ShouldThrow_WhenSmtpConfigurationIsMissing()
    {
        var sender = new SmtpEmailSender(CreateOptions());

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
        var sender = new SmtpEmailSender(CreateOptions(
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

    [Fact]
    public void BuildMessage_ShouldRenderVerificationEmailContent()
    {
        var sender = new SmtpEmailSender(CreateOptions(username: "noreply@example.com"));

        var message = InvokeBuildMessage(sender, new EmailMessage
        {
            Email = "member@example.com",
            Token = "verify token/+",
            Type = EmailMessageType.VerifyEmail,
            Code = "123456"
        });

        Assert.Equal("Verify your email", message.Subject);
        Assert.Equal("noreply@example.com", message.From.Mailboxes.Single().Address);
        Assert.Equal("member@example.com", message.To.Mailboxes.Single().Address);
        Assert.Contains("/auth/verify?token=verify%20token%2F%2B", message.TextBody);
        Assert.Contains("Verification code: 123456", message.TextBody);
        Assert.Contains("Verify email", message.HtmlBody);
        Assert.Contains("123456", message.HtmlBody);
    }

    [Fact]
    public void BuildMessage_ShouldRenderEventInviteFallbackTitle()
    {
        var sender = new SmtpEmailSender(CreateOptions(username: "noreply@example.com"));

        var message = InvokeBuildMessage(sender, new EmailMessage
        {
            Email = "member@example.com",
            Token = "invite-token",
            Type = EmailMessageType.EventInvite
        });

        Assert.Equal("You're invited to a private event", message.Subject);
        Assert.Contains("View invitation", message.HtmlBody);
        Assert.Contains("/events/invite?token=invite-token", message.TextBody);
    }

    [Fact]
    public void BuildMessage_ShouldThrowForUnsupportedEmailType()
    {
        var sender = new SmtpEmailSender(CreateOptions(username: "noreply@example.com"));

        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeBuildMessage(sender, new EmailMessage
            {
                Email = "member@example.com",
                Token = "verify-token",
                Type = (EmailMessageType)999
            }));

        Assert.Equal("Unsupported email type '999'.", exception.InnerException?.Message);
    }

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

    private static MimeKit.MimeMessage InvokeBuildMessage(SmtpEmailSender sender, EmailMessage message)
    {
        var method = typeof(SmtpEmailSender).GetMethod("BuildMessage", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildMessage method not found.");

        return (MimeKit.MimeMessage)method.Invoke(sender, [message])!;
    }
}
