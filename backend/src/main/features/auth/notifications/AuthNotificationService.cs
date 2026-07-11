using backend.main.shared.providers;
using backend.main.shared.providers.messages;

namespace backend.main.features.auth.notifications
{
    public sealed class AuthNotificationService : IAuthNotificationService
    {
        private readonly IPublisher _publisher;

        public AuthNotificationService(IPublisher publisher)
        {
            _publisher = publisher;
        }

        public Task SendSignupVerificationAsync(
            string email,
            string token,
            string code,
            string? recipientName = null)
        {
            return _publisher.PublishAsync(
                NotificationTopics.Email,
                new EmailMessage
                {
                    Type = EmailMessageType.VerifyEmail,
                    Email = email,
                    Token = token,
                    Code = code,
                    RecipientName = recipientName
                });
        }

        public Task SendPasswordResetAsync(
            string email,
            string token,
            string code,
            string? recipientName = null)
        {
            return _publisher.PublishAsync(
                NotificationTopics.Email,
                new EmailMessage
                {
                    Type = EmailMessageType.ResetPassword,
                    Email = email,
                    Token = token,
                    RecipientName = recipientName,
                    Code = code
                });
        }

        public Task SendDeviceVerificationAsync(
            string email,
            string token,
            string? recipientName = null)
        {
            return _publisher.PublishAsync(
                NotificationTopics.Email,
                new EmailMessage
                {
                    Type = EmailMessageType.NewDevice,
                    Email = email,
                    Token = token,
                    RecipientName = recipientName
                });
        }

        public Task SendSmsMfaAsync(
            string phoneNumber,
            string code,
            string challenge,
            DateTime expiresAtUtc,
            string purpose)
        {
            return _publisher.PublishAsync(
                NotificationTopics.Sms,
                new SmsMfaMessage
                {
                    PhoneNumber = phoneNumber,
                    Code = code,
                    Challenge = challenge,
                    ExpiresAtUtc = expiresAtUtc,
                    Purpose = purpose
                });
        }

        public Task SendEmailMfaCodeAsync(
            string email,
            string code,
            string? recipientName = null)
        {
            return _publisher.PublishAsync(
                NotificationTopics.Email,
                new EmailMessage
                {
                    Type = EmailMessageType.MfaCode,
                    Email = email,
                    Code = code,
                    RecipientName = recipientName
                });
        }
    }
}
