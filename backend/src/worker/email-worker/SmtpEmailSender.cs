using System.Net.Mail;

using backend.main.dtos;

using MailKit.Security;
using MailKitSmtpClient = MailKit.Net.Smtp.SmtpClient;

using MimeKit;

namespace backend.worker.email_worker;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailWorkerOptions _options;

    public SmtpEmailSender(EmailWorkerOptions options)
    {
        _options = options;
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

        var (subject, plainText, html) = BuildContent(message);
        mail.Subject = subject;
        mail.Body = new BodyBuilder
        {
            TextBody = plainText,
            HtmlBody = html
        }.ToMessageBody();

        return mail;
    }

    private (string Subject, string PlainText, string Html) BuildContent(EmailMessage message)
    {
        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var verifyUrl = $"{baseUrl}/auth/verify?token={Uri.EscapeDataString(message.Token)}";
        var deviceUrl = $"{baseUrl}/auth/device/verify?token={Uri.EscapeDataString(message.Token)}";
        var resetUrl = $"{baseUrl}/auth/change-password?token={Uri.EscapeDataString(message.Token)}";

        return message.Type switch
        {
            EmailMessageType.VerifyEmail => (
                "Verify your email",
                BuildPlainText("Verify your email", verifyUrl, message.Code),
                BuildHtml("Verify your email", verifyUrl, "Verify email", message.Code)
            ),
            EmailMessageType.ResetPassword => (
                "Reset your password",
                BuildPlainText("Reset your password", resetUrl, message.Code),
                BuildHtml("Reset your password", resetUrl, "Reset password", message.Code)
            ),
            EmailMessageType.NewDevice => (
                "Confirm new device sign-in",
                BuildPlainText("Confirm this device sign-in", deviceUrl, null),
                BuildHtml("Confirm this device sign-in", deviceUrl, "Verify device", null)
            ),
            EmailMessageType.AccountConfirmation => (
                "Account confirmation",
                BuildPlainText("Confirm your account", verifyUrl, message.Code),
                BuildHtml("Confirm your account", verifyUrl, "Confirm account", message.Code)
            ),
            _ => throw new InvalidOperationException($"Unsupported email type '{message.Type}'.")
        };
    }

    private static string BuildPlainText(string title, string url, string? code)
    {
        var lines = new List<string>
        {
            title,
            "",
            $"Open this link: {url}"
        };

        if (!string.IsNullOrWhiteSpace(code))
        {
            lines.Add("");
            lines.Add($"Verification code: {code}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildHtml(string title, string url, string buttonText, string? code)
    {
        var codeMarkup = string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : $"<p><strong>Verification code:</strong> {System.Net.WebUtility.HtmlEncode(code)}</p>";

        return $"""
        <html>
          <body style="font-family: Arial, sans-serif; line-height: 1.5; color: #111827;">
            <h2>{System.Net.WebUtility.HtmlEncode(title)}</h2>
            <p>
              <a href="{System.Net.WebUtility.HtmlEncode(url)}" style="display:inline-block;padding:10px 16px;background:#111827;color:#ffffff;text-decoration:none;border-radius:6px;">
                {System.Net.WebUtility.HtmlEncode(buttonText)}
              </a>
            </p>
            <p>{System.Net.WebUtility.HtmlEncode(url)}</p>
            {codeMarkup}
          </body>
        </html>
        """;
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
