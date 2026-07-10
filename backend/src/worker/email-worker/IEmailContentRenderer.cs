using backend.main.shared.providers.messages;

namespace backend.worker.email_worker;

/// <summary>
/// Renders an <see cref="EmailMessage"/> into a deliverable subject line plus
/// matching plain-text and HTML bodies. Keeping composition separate from the
/// SMTP transport keeps rendering easy to unit-test and extend with new types.
/// </summary>
public interface IEmailContentRenderer
{
    EmailContent Render(EmailMessage message);
}

/// <summary>The rendered pieces of an email, ready to hand to the transport.</summary>
public sealed record EmailContent(string Subject, string PlainText, string Html);
