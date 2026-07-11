using System.Globalization;
using System.Text;

using backend.main.shared.providers.messages;

namespace backend.worker.email_worker;

/// <summary>
/// Composes branded subject/plain-text/HTML content for every
/// <see cref="EmailMessageType"/>. HTML is wrapped in the shared
/// <see cref="EmailLayout"/> so all message types share one consistent look.
/// </summary>
public sealed class EmailTemplateRenderer : IEmailContentRenderer
{
    private readonly EmailWorkerOptions _options;

    public EmailTemplateRenderer(EmailWorkerOptions options)
    {
        _options = options;
    }

    public EmailContent Render(EmailMessage message)
    {
        var content = BuildContent(message);
        return new EmailContent(content.Subject, RenderPlainText(content), RenderHtml(content));
    }

    private Content BuildContent(EmailMessage message)
    {
        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var greeting = string.IsNullOrWhiteSpace(message.RecipientName)
            ? "Hi there,"
            : $"Hi {message.RecipientName!.Trim()},";
        var eventName = string.IsNullOrWhiteSpace(message.EventName) ? "a private event" : message.EventName!.Trim();

        return message.Type switch
        {
            EmailMessageType.VerifyEmail => new Content(
                Subject: "Verify your email",
                Preheader: "Confirm your email address to finish setting up your account.",
                Heading: "Verify your email",
                Greeting: greeting,
                Intro: ["Welcome to EventXperience! Please confirm your email address to activate your account."],
                Cta: new Cta(BuildUrl(baseUrl, "/auth/verify", RequireToken(message)), "Verify email"),
                Code: message.Code,
                MutedNote: "This link will expire soon. If you didn't create an account, you can safely ignore this email."),

            EmailMessageType.AccountConfirmation => new Content(
                Subject: "Confirm your account",
                Preheader: "Confirm your EventXperience account to get started.",
                Heading: "Confirm your account",
                Greeting: greeting,
                Intro: ["Please confirm your account to finish signing up for EventXperience."],
                Cta: new Cta(BuildUrl(baseUrl, "/auth/verify", RequireToken(message)), "Confirm account"),
                Code: message.Code,
                MutedNote: "This link will expire soon. If you didn't create an account, you can safely ignore this email."),

            EmailMessageType.ResetPassword => new Content(
                Subject: "Reset your password",
                Preheader: "Use the link below to choose a new password.",
                Heading: "Reset your password",
                Greeting: greeting,
                Intro: ["We received a request to reset your password. Choose a new one using the button below."],
                Cta: new Cta(BuildUrl(baseUrl, "/auth/change-password", RequireToken(message)), "Reset password"),
                Code: message.Code,
                MutedNote: "If you didn't request a password reset, you can safely ignore this email — your password won't change."),

            EmailMessageType.NewDevice => new Content(
                Subject: "Confirm new device sign-in",
                Preheader: "A new device tried to sign in to your account.",
                Heading: "Confirm this device sign-in",
                Greeting: greeting,
                Intro: ["We noticed a sign-in from a new device. Confirm it was you to continue."],
                Cta: new Cta(BuildUrl(baseUrl, "/auth/device/verify", RequireToken(message)), "Verify device"),
                Code: null,
                MutedNote: "If this wasn't you, please reset your password immediately and review your account security."),

            EmailMessageType.MfaCode => new Content(
                Subject: "Your verification code",
                Preheader: "Use this code to verify it's you.",
                Heading: "Verify it's you",
                Greeting: greeting,
                Intro: ["Enter the code below to continue. It expires shortly, so use it soon."],
                Cta: null,
                Code: RequireCode(message),
                MutedNote: "If you didn't request this code, you can safely ignore this email — no action is needed."),

            EmailMessageType.EventInvite => new Content(
                Subject: $"You're invited to {eventName}",
                Preheader: $"You've been invited to {eventName} on EventXperience.",
                Heading: $"You're invited to {eventName}",
                Greeting: greeting,
                Intro: [$"You've been invited to {eventName}. View the invitation to see the details and respond."],
                Cta: new Cta(BuildUrl(baseUrl, "/events/invite", RequireToken(message)), "View invitation"),
                Code: null,
                MutedNote: "If you weren't expecting this invitation, you can safely ignore this email."),

            EmailMessageType.Welcome => new Content(
                Subject: "Welcome to EventXperience",
                Preheader: "Your account is ready — start exploring events.",
                Heading: "Welcome to EventXperience!",
                Greeting: greeting,
                Intro:
                [
                    "Your account is all set up. EventXperience is a modern platform for creating, managing, and scaling unforgettable event experiences.",
                    "Browse upcoming events or create your own to get started."
                ],
                Cta: new Cta($"{baseUrl}/events", "Browse events"),
                Code: null,
                MutedNote: "Need a hand? Just reply to this email and we'll be happy to help."),

            EmailMessageType.PasswordChanged => new Content(
                Subject: "Your password was changed",
                Preheader: "This is a confirmation that your password was updated.",
                Heading: "Your password was changed",
                Greeting: greeting,
                Intro: ["This is a confirmation that the password for your EventXperience account was successfully changed."],
                Cta: null,
                Code: null,
                MutedNote: "If you didn't make this change, please reset your password immediately and contact support."),

            EmailMessageType.InvitationAccepted => new Content(
                Subject: $"{ActorLabel(message)} accepted your invitation to {eventName}",
                Preheader: $"{ActorLabel(message)} is coming to {eventName}.",
                Heading: "Invitation accepted",
                Greeting: greeting,
                Intro: [$"Good news — {ActorLabel(message)} accepted your invitation to {eventName}."],
                Cta: new Cta($"{baseUrl}/events", "View event"),
                Code: null,
                MutedNote: null),

            EmailMessageType.InvitationDeclined => new Content(
                Subject: $"{ActorLabel(message)} declined your invitation to {eventName}",
                Preheader: $"{ActorLabel(message)} won't be attending {eventName}.",
                Heading: "Invitation declined",
                Greeting: greeting,
                Intro: [$"{ActorLabel(message)} declined your invitation to {eventName}."],
                Cta: new Cta($"{baseUrl}/events", "View event"),
                Code: null,
                MutedNote: null),

            EmailMessageType.EventReminder => new Content(
                Subject: $"Reminder: {eventName} is coming up",
                Preheader: $"Don't forget — {eventName} is on the way.",
                Heading: $"{eventName} is coming up",
                Greeting: greeting,
                Intro: [BuildReminderIntro(eventName, message.EventStartsAtUtc)],
                Cta: new Cta($"{baseUrl}/events", "View event"),
                Code: null,
                MutedNote: null),

            _ => throw new InvalidOperationException($"Unsupported email type '{message.Type}'.")
        };
    }

    private static string BuildReminderIntro(string eventName, DateTime? startsAtUtc)
    {
        if (startsAtUtc is null)
            return $"This is a friendly reminder that {eventName} is coming up soon.";

        var when = startsAtUtc.Value.ToUniversalTime()
            .ToString("dddd, dd MMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);
        return $"This is a friendly reminder that {eventName} starts on {when}.";
    }

    private static string ActorLabel(EmailMessage message) =>
        string.IsNullOrWhiteSpace(message.ActorName) ? "A guest" : message.ActorName!.Trim();

    private static string RequireCode(EmailMessage message) =>
        string.IsNullOrWhiteSpace(message.Code)
            ? throw new InvalidOperationException($"Email type '{message.Type}' requires a code.")
            : message.Code!;

    private static string RequireToken(EmailMessage message) =>
        string.IsNullOrWhiteSpace(message.Token)
            ? throw new InvalidOperationException($"Email type '{message.Type}' requires a token.")
            : message.Token!;

    private static string BuildUrl(string baseUrl, string path, string token) =>
        $"{baseUrl}{path}?token={Uri.EscapeDataString(token)}";

    private static string RenderHtml(Content content)
    {
        var body = new StringBuilder();
        body.Append(EmailLayout.Heading(content.Heading));
        body.Append(EmailLayout.Paragraph(content.Greeting));

        foreach (var paragraph in content.Intro)
            body.Append(EmailLayout.Paragraph(paragraph));

        if (!string.IsNullOrWhiteSpace(content.Code))
            body.Append(EmailLayout.CodeBlock(content.Code!));

        if (content.Cta is not null)
        {
            body.Append(EmailLayout.Button(content.Cta.Url, content.Cta.Label));
            body.Append(EmailLayout.LinkFallback(content.Cta.Url));
        }

        if (!string.IsNullOrWhiteSpace(content.MutedNote))
            body.Append(EmailLayout.MutedNote(content.MutedNote!));

        return EmailLayout.Document(content.Preheader, body.ToString());
    }

    private static string RenderPlainText(Content content)
    {
        var lines = new List<string> { content.Heading, string.Empty, content.Greeting, string.Empty };
        lines.AddRange(content.Intro);

        if (!string.IsNullOrWhiteSpace(content.Code))
        {
            lines.Add(string.Empty);
            lines.Add($"Verification code: {content.Code}");
        }

        if (content.Cta is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"{content.Cta.Label}: {content.Cta.Url}");
        }

        if (!string.IsNullOrWhiteSpace(content.MutedNote))
        {
            lines.Add(string.Empty);
            lines.Add(content.MutedNote!);
        }

        lines.Add(string.Empty);
        lines.Add("— EventXperience");

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record Content(
        string Subject,
        string Preheader,
        string Heading,
        string Greeting,
        IReadOnlyList<string> Intro,
        Cta? Cta,
        string? Code,
        string? MutedNote);

    private sealed record Cta(string Url, string Label);
}
