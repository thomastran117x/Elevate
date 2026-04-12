using System.IO;

using MailKit.Net.Smtp;
using MailKit.Security;

using MimeKit;

using Polly;
using Polly.Retry;

using worker.Config;
using worker.Interfaces;
using worker.Utilities;

namespace worker.Services
{
    public sealed class EmailService : IEmailService
    {
        private readonly string _smtpHost = "smtp.gmail.com";
        private readonly int _smtpPort = 587;
        private readonly string? _username;
        private readonly string? _appPassword;

        private readonly AsyncRetryPolicy? _smtpRetryPolicy;
        private bool IsConfigured { get; }

        public EmailService()
        {
            _username = EnvManager.Email;
            _appPassword = EnvManager.Password;

            if (string.IsNullOrWhiteSpace(_username)
                || string.IsNullOrWhiteSpace(_appPassword))
            {
                Logger.Warn("[EmailService] EMAIL or EMAIL_PASSWORD not configured.");
                IsConfigured = false;
                return;
            }

            _smtpRetryPolicy = Policy
                .Handle<SmtpCommandException>()
                .Or<SmtpProtocolException>()
                .Or<IOException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, attempt))
                        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)),
                    onRetry: (ex, delay, attempt, _) =>
                    {
                        Logger.Warn(
                            $"[EmailService] SMTP retry {attempt}/3 in {delay.TotalSeconds:F1}s - {ex.Message}"
                        );
                    }
                );

            try
            {
                IsConfigured = EmailSmokeTest().GetAwaiter().GetResult();
                if (!IsConfigured)
                    Logger.Warn("[EmailService] SMTP smoke test failed.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[EmailService] Initialization failed");
                IsConfigured = false;
            }
        }

        public async Task SendVerificationEmailAsync(string toEmail, string token, string? code = null)
        {
            EnsureEnabled();

            var verifyUrl =
                $"http://localhost:3090/auth/verify?token={Uri.EscapeDataString(token)}";

            await SendAsync(
                BuildActionMessage(
                    toEmail,
                    "Verify Your Email Address",
                    "Welcome to EventXperience",
                    "Thanks for signing up for EventXperience. Confirm your email using the code below in the app, or continue with the secure link.",
                    "Verify My Email",
                    verifyUrl,
                    code,
                    "verification",
                    "This verification code and link expire in 30 minutes."
                )
            );
        }

        public async Task SendResetPasswordEmailAsync(string toEmail, string token, string? code = null)
        {
            EnsureEnabled();

            var resetUrl =
                $"http://localhost:3090/auth/change-password?token={Uri.EscapeDataString(token)}";

            await SendAsync(
                BuildActionMessage(
                    toEmail,
                    "Reset Your Password",
                    "Reset Your Password",
                    "We received a request to reset your EventXperience password. Use the code below in the app, or continue with the secure reset link.",
                    "Reset Password",
                    resetUrl,
                    code,
                    "password reset",
                    "This reset code and link expire in 30 minutes."
                )
            );
        }

        public async Task SendNewDeviceEmailAsync(string toEmail, string token)
        {
            EnsureEnabled();

            var verifyUrl =
                $"http://localhost:3090/auth/device/verify?token={Uri.EscapeDataString(token)}";

            await SendAsync(
                BuildActionMessage(
                    toEmail,
                    "New Device Login Detected",
                    "New Device Detected",
                    "We noticed a login attempt from a device we do not recognise. If this was you, confirm the device below to complete the login.",
                    "Verify This Device",
                    verifyUrl,
                    null,
                    "device verification",
                    "This verification link expires in 15 minutes."
                )
            );
        }

        public Task SendConfirmationEmailAsync(string toEmail, string token)
        {
            EnsureEnabled();
            throw new NotImplementedException();
        }

        public bool isEmailEnabled() => IsConfigured;

        private async Task SendAsync(MimeMessage message)
        {
            await _smtpRetryPolicy!.ExecuteAsync(async () =>
            {
                using var client = new SmtpClient();

                await client.ConnectAsync(
                    _smtpHost,
                    _smtpPort,
                    SecureSocketOptions.StartTls
                );

                await client.AuthenticateAsync(_username, _appPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            });
        }

        private async Task<bool> EmailSmokeTest()
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("EventXperience System", _username));
            message.To.Add(MailboxAddress.Parse(_username!));
            message.Subject = "Email Service Smoke Test";
            message.Body = new TextPart("plain")
            {
                Text =
                    $"This is a test email to confirm SMTP configuration.\n\n" +
                    $"Timestamp: {DateTime.UtcNow:O}"
            };

            try
            {
                await SendAsync(message);
                Logger.Info("[EmailService] SMTP smoke test successful.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[EmailService] SMTP smoke test failed");
                return false;
            }
        }

        private static MimeMessage BuildActionMessage(
            string toEmail,
            string subject,
            string title,
            string intro,
            string buttonLabel,
            string actionUrl,
            string? code,
            string codeLabel,
            string footerNote
        )
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("EventXperience Team", EnvManager.Email));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var otpHtml = string.IsNullOrWhiteSpace(code)
                ? string.Empty
                : $@"
                    <div style='margin:28px 0;padding:24px;border-radius:16px;background:#f5f3ff;border:1px solid #ddd6fe;text-align:center;'>
                        <p style='margin:0 0 10px;font-size:14px;color:#6b7280;'>Your {codeLabel} code</p>
                        <div style='font-size:32px;letter-spacing:8px;font-weight:700;color:#4c1d95;'>{code}</div>
                    </div>";

            var otpText = string.IsNullOrWhiteSpace(code)
                ? string.Empty
                : $"Your {codeLabel} code is: {code}\n\n";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
            <!DOCTYPE html>
            <html>
            <body style='margin:0;padding:0;background-color:#f5f3ff;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;'>
                <div style='max-width:640px;margin:48px auto;padding:0 16px;'>
                    <div style='background:#ffffff;border-radius:18px;box-shadow:0 12px 30px rgba(88,80,236,0.18);overflow:hidden;'>
                        <div style='background:linear-gradient(135deg,#6d28d9,#7c3aed,#8b5cf6);padding:36px 28px;text-align:center;color:#ffffff;'>
                            <h1 style='margin:0;font-size:26px;font-weight:700;letter-spacing:0.4px;'>{title}</h1>
                        </div>

                        <div style='padding:40px 34px;color:#1f2937;'>
                            <p style='font-size:17px;margin:0 0 18px;'>Hi there</p>
                            <p style='font-size:16px;line-height:1.65;margin:0 0 22px;color:#374151;'>{intro}</p>
                            {otpHtml}
                            <div style='text-align:center;margin:36px 0;'>
                                <a href='{actionUrl}'
                                style='display:inline-block;background:linear-gradient(135deg,#7c3aed,#8b5cf6);color:#ffffff;padding:16px 34px;font-size:16px;font-weight:600;border-radius:12px;text-decoration:none;box-shadow:0 6px 16px rgba(124,58,237,0.45);'>
                                {buttonLabel}
                                </a>
                            </div>
                            <p style='font-size:14px;color:#6b7280;line-height:1.6;margin:0;'>
                                {footerNote}
                            </p>
                        </div>
                    </div>
                </div>
            </body>
            </html>",
                TextBody =
                    $"{title}\n\n" +
                    $"{intro}\n\n" +
                    otpText +
                    $"Continue here:\n{actionUrl}\n\n" +
                    $"{footerNote}\n\n" +
                    $"- The EventXperience Team"
            };

            message.Body = builder.ToMessageBody();

            return message;
        }

        private void EnsureEnabled()
        {
            if (!IsConfigured)
            {
                Logger.Warn("[EmailService] Email service disabled.");
                throw new InvalidOperationException("Email service is not configured");
            }
        }
    }
}
