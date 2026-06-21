using backend.main.features.auth.notifications;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Features.Auth;

public class AuthNotificationServiceTests
{
    [Fact]
    public async Task SendSignupVerificationAsync_ShouldPublishVerifyEmailMessage()
    {
        var publisher = new Mock<IPublisher>();
        var service = new AuthNotificationService(publisher.Object);

        await service.SendSignupVerificationAsync("member@example.com", "verify-token", "123456");

        publisher.Verify(p => p.PublishAsync(
            NotificationTopics.Email,
            It.Is<EmailMessage>(message =>
                message.Type == EmailMessageType.VerifyEmail
                && message.Email == "member@example.com"
                && message.Token == "verify-token"
                && message.Code == "123456")), Times.Once);
    }

    [Fact]
    public async Task SendSmsMfaAsync_ShouldPublishSmsPayload()
    {
        var publisher = new Mock<IPublisher>();
        var service = new AuthNotificationService(publisher.Object);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(5);

        await service.SendSmsMfaAsync("+14165550123", "654321", "challenge-1", expiresAtUtc, "mfa");

        publisher.Verify(p => p.PublishAsync(
            NotificationTopics.Sms,
            It.Is<SmsMfaMessage>(message =>
                message.PhoneNumber == "+14165550123"
                && message.Code == "654321"
                && message.Challenge == "challenge-1"
                && message.Purpose == "mfa"
                && message.ExpiresAtUtc == expiresAtUtc)), Times.Once);
    }
}
