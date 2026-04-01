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

        private readonly AsyncRetryPolicy _smtpRetryPolicy;
        private bool IsConfigured { get; }

        public EmailService()
        {
            _username = EnvManager.Email;
            _appPassword = EnvManager.Password;

            if (string.IsNullOrWhiteSpace(_username) ||
                string.IsNullOrWhiteSpace(_appPassword))
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
                        TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                        TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)),
                    onRetry: (ex, delay, attempt, _) =>
                    {
                        Logger.Warn(
                            $"[EmailService] SMTP retry {attempt}/3 in {delay.TotalSeconds:F1}s — {ex.Message}"
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

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public async Task SendVerificationEmailAsync(string toEmail, string token)
        {
            EnsureEnabled();

            var verifyUrl =
                $"http://localhost:3090/auth/verify?token={Uri.EscapeDataString(token)}";

            var message = BuildVerificationMessage(toEmail, verifyUrl);

            await SendAsync(message);
        }

        public async Task SendNewDeviceEmailAsync(string toEmail, string token)
        {
            EnsureEnabled();

            var verifyUrl =
                $"http://localhost:3090/auth/device/verify?token={Uri.EscapeDataString(token)}";

            var message = BuildNewDeviceMessage(toEmail, verifyUrl);

            await SendAsync(message);
        }

        public Task SendResetPasswordEmailAsync(string toEmail, string token)
        {
            EnsureEnabled();
            throw new NotImplementedException();
        }

        public Task SendConfirmationEmailAsync(string toEmail, string token)
        {
            EnsureEnabled();
            throw new NotImplementedException();
        }

        public bool isEmailEnabled() => IsConfigured;

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------

        private async Task SendAsync(MimeMessage message)
        {
            await _smtpRetryPolicy.ExecuteAsync(async () =>
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

        private static MimeMessage BuildVerificationMessage(
            string toEmail,
            string verifyUrl
        )
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("EventXperience Team", EnvManager.Email));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Verify Your Email Address";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
            <!DOCTYPE html>
            <html>
            <body style='margin:0;padding:0;background-color:#f5f3ff;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;'>
                <div style='max-width:640px;margin:48px auto;padding:0 16px;'>

                <!-- Card -->
                <div style='
                    background:#ffffff;
                    border-radius:18px;
                    box-shadow:0 12px 30px rgba(88,80,236,0.18);
                    overflow:hidden;
                '>

                    <!-- Header -->
                    <div style='
                    background:linear-gradient(135deg,#6d28d9,#7c3aed,#8b5cf6);
                    padding:36px 28px;
                    text-align:center;
                    color:#ffffff;
                    '>
                    <h1 style='
                        margin:0;
                        font-size:26px;
                        font-weight:700;
                        letter-spacing:0.4px;
                    '>
                        Welcome to EventXperience
                    </h1>
                    <p style='
                        margin:10px 0 0;
                        font-size:15px;
                        opacity:0.95;
                    '>
                        Discover events. Create moments. ✨
                    </p>
                    </div>

                    <!-- Body -->
                    <div style='padding:40px 34px;color:#1f2937;'>
                    <p style='font-size:17px;margin:0 0 18px;'>
                        Hi there 👋
                    </p>

                    <p style='font-size:16px;line-height:1.65;margin:0 0 22px;color:#374151;'>
                        Thanks for signing up for <strong>EventXperience</strong>!
                        Before you get started, please confirm your email address.
                    </p>

                    <!-- CTA -->
                    <div style='text-align:center;margin:36px 0;'>
                        <a href='{verifyUrl}'
                        style='
                            display:inline-block;
                            background:linear-gradient(135deg,#7c3aed,#8b5cf6);
                            color:#ffffff;
                            padding:16px 34px;
                            font-size:16px;
                            font-weight:600;
                            border-radius:12px;
                            text-decoration:none;
                            box-shadow:0 6px 16px rgba(124,58,237,0.45);
                        '>
                        Verify My Email
                        </a>
                    </div>

                    <p style='font-size:14px;color:#6b7280;line-height:1.6;margin:0 0 12px;'>
                        This verification link will expire in <strong>10 minutes</strong> for security reasons.
                    </p>

                    <p style='font-size:14px;color:#6b7280;line-height:1.6;margin:0;'>
                        If you didn’t create an EventXperience account, you can safely ignore this email.
                    </p>
                    </div>

                    <!-- Divider -->
                    <div style='height:1px;background:#e5e7eb;margin:0 34px;'></div>

                    <!-- Footer -->
                    <div style='padding:24px 34px 32px;text-align:center;'>
                    <p style='
                        font-size:14px;
                        color:#4f46e5;
                        font-weight:600;
                        margin:0 0 8px;
                    '>
                        — The EventXperience Team
                    </p>
                    <p style='
                        font-size:12px;
                        color:#9ca3af;
                        margin:0;
                    '>
                        © {DateTime.UtcNow.Year} EventXperience. All rights reserved.
                    </p>
                    </div>

                </div>
                </div>
            </body>
            </html>
            ",

                TextBody =
                    $"Welcome to EventXperience!\n\n" +
                    $"Please verify your email address using the link below:\n\n" +
                    $"{verifyUrl}\n\n" +
                    $"This link expires in 10 minutes.\n\n" +
                    $"— The EventXperience Team"
            };

            message.Body = builder.ToMessageBody();

            return message;
        }

        private static MimeMessage BuildNewDeviceMessage(string toEmail, string verifyUrl)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("EventXperience Team", EnvManager.Email));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "New Device Login Detected";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
            <!DOCTYPE html>
            <html>
            <body style='margin:0;padding:0;background-color:#f5f3ff;font-family:-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;'>
                <div style='max-width:640px;margin:48px auto;padding:0 16px;'>

                <!-- Card -->
                <div style='
                    background:#ffffff;
                    border-radius:18px;
                    box-shadow:0 12px 30px rgba(88,80,236,0.18);
                    overflow:hidden;
                '>

                    <!-- Header -->
                    <div style='
                    background:linear-gradient(135deg,#6d28d9,#7c3aed,#8b5cf6);
                    padding:36px 28px;
                    text-align:center;
                    color:#ffffff;
                    '>
                    <h1 style='
                        margin:0;
                        font-size:26px;
                        font-weight:700;
                        letter-spacing:0.4px;
                    '>
                        New Device Detected
                    </h1>
                    <p style='
                        margin:10px 0 0;
                        font-size:15px;
                        opacity:0.95;
                    '>
                        Someone signed in from a new device
                    </p>
                    </div>

                    <!-- Body -->
                    <div style='padding:40px 34px;color:#1f2937;'>
                    <p style='font-size:17px;margin:0 0 18px;'>
                        Hi there 👋
                    </p>

                    <p style='font-size:16px;line-height:1.65;margin:0 0 22px;color:#374151;'>
                        We noticed a login attempt to your <strong>EventXperience</strong> account from a device we don't recognise.
                        If this was you, please verify the device below to complete your login.
                    </p>

                    <!-- CTA -->
                    <div style='text-align:center;margin:36px 0;'>
                        <a href='{verifyUrl}'
                        style='
                            display:inline-block;
                            background:linear-gradient(135deg,#7c3aed,#8b5cf6);
                            color:#ffffff;
                            padding:16px 34px;
                            font-size:16px;
                            font-weight:600;
                            border-radius:12px;
                            text-decoration:none;
                            box-shadow:0 6px 16px rgba(124,58,237,0.45);
                        '>
                        Verify This Device
                        </a>
                    </div>

                    <p style='font-size:14px;color:#6b7280;line-height:1.6;margin:0 0 12px;'>
                        This link will expire in <strong>15 minutes</strong>. If you did not attempt to log in, you can safely ignore this email and your account will remain secure.
                    </p>
                    </div>

                    <!-- Divider -->
                    <div style='height:1px;background:#e5e7eb;margin:0 34px;'></div>

                    <!-- Footer -->
                    <div style='padding:24px 34px 32px;text-align:center;'>
                    <p style='
                        font-size:14px;
                        color:#4f46e5;
                        font-weight:600;
                        margin:0 0 8px;
                    '>
                        — The EventXperience Team
                    </p>
                    <p style='
                        font-size:12px;
                        color:#9ca3af;
                        margin:0;
                    '>
                        © {DateTime.UtcNow.Year} EventXperience. All rights reserved.
                    </p>
                    </div>

                </div>
                </div>
            </body>
            </html>
            ",

                TextBody =
                    $"EventXperience — New Device Login\n\n" +
                    $"We noticed a login attempt from a new device. Click the link below to verify it:\n\n" +
                    $"{verifyUrl}\n\n" +
                    $"This link expires in 15 minutes.\n\n" +
                    $"If you did not attempt to log in, ignore this email.\n\n" +
                    $"— The EventXperience Team"
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
