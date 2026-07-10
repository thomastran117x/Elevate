using System.Net.Mail;

using backend.main.shared.providers.messages;

using MailKit.Security;

using MimeKit;

using MailKitSmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace backend.worker.email_worker;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailWorkerOptions _options;
    private readonly IEmailContentRenderer _renderer;

    public SmtpEmailSender(EmailWorkerOptions options, IEmailContentRenderer renderer)
    {
        _options = options;
        _renderer = renderer;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Email worker is missing SMTP configuration.");

        ValidateEmailAddress(message.Email);

        var email = BuildMessage(message);

        using var client = new MailKitSmtpClient();
        var socketOptions = _options.SmtpPort == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(_options.SmtpServer!, _options.SmtpPort, socketOptions, cancellationToken);
        await client.AuthenticateAsync(_options.Username!, _options.Password!, cancellationToken);
        await client.SendAsync(email, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private MimeMessage BuildMessage(EmailMessage message)
    {
        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(_options.Username!));
        mail.To.Add(MailboxAddress.Parse(message.Email));

        var content = _renderer.Render(message);
        mail.Subject = content.Subject;
        mail.Body = new BodyBuilder
        {
            TextBody = content.PlainText,
            HtmlBody = content.Html
        }.ToMessageBody();

        return mail;
    }

    private static void ValidateEmailAddress(string email)
    {
        try
        {
            _ = new MailAddress(email);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Email payload contains an invalid recipient address.", ex);
        }
    }
}
