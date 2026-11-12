using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

using backend.main.shared.providers.messages;

namespace backend.main.shared.providers.messaging;

public enum NotificationDeliveryOutcome
{
    /// <summary>The provider accepted the message.</summary>
    Sent,

    /// <summary>Delivery failed after retries; the message went to the DLQ.</summary>
    Failed,

    /// <summary>The payload could not be read; the message went to the DLQ.</summary>
    Malformed,
}

/// <summary>
/// Delivery outcomes on the <c>EventXperience.Notifications</c> meter, recorded by the email and
/// SMS workers. This is the delivery half of the recovery and MFA dashboards: the API records
/// that a code or link was issued, and these counters record whether it reached the provider.
/// </summary>
/// <remarks>
/// Tags come from closed sets only: the email message type and the outcome. Recipients,
/// addresses, phone numbers, and provider error text never become tags.
/// </remarks>
public sealed class NotificationDeliveryMetrics
{
    public const string MeterName = "EventXperience.Notifications";

    private static readonly FrozenDictionary<EmailMessageType, string> MessageTypeNames =
        Enum.GetValues<EmailMessageType>().ToFrozenDictionary(type => type, type => SnakeCase(type.ToString()));

    private readonly Counter<long> _emailDeliveries;
    private readonly Counter<long> _smsDeliveries;

    public NotificationDeliveryMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _emailDeliveries = meter.CreateCounter<long>(
            "notifications.email.deliveries",
            unit: "{message}",
            description: "Email delivery attempts by message type and outcome.");
        _smsDeliveries = meter.CreateCounter<long>(
            "notifications.sms.deliveries",
            unit: "{message}",
            description: "SMS MFA code delivery attempts by outcome.");
    }

    public void RecordEmail(EmailMessageType? type, NotificationDeliveryOutcome outcome) =>
        _emailDeliveries.Add(1, new TagList
        {
            { "message_type", type is { } known && MessageTypeNames.TryGetValue(known, out var name) ? name : "unknown" },
            { "outcome", WireName(outcome) },
        });

    public void RecordSms(NotificationDeliveryOutcome outcome) =>
        _smsDeliveries.Add(1, new KeyValuePair<string, object?>("outcome", WireName(outcome)));

    public static string WireName(EmailMessageType type) => MessageTypeNames[type];

    public static string WireName(NotificationDeliveryOutcome outcome) => outcome switch
    {
        NotificationDeliveryOutcome.Sent => "sent",
        NotificationDeliveryOutcome.Failed => "failed",
        NotificationDeliveryOutcome.Malformed => "malformed",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    private static string SnakeCase(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 4);
        for (var index = 0; index < pascal.Length; index++)
        {
            if (index > 0 && char.IsUpper(pascal[index]))
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(pascal[index]));
        }

        return builder.ToString();
    }
}
