using backend.main.features.events.invitations;

namespace backend.main.shared.providers.messages;

public sealed class EmailDeliveryStatusMessage
{
    public EmailMessageType Type
    {
        get; init;
    }
    public int? EventInvitationId
    {
        get; init;
    }
    public EventInvitationDeliveryStatus DeliveryStatus
    {
        get; init;
    }
    public string? ErrorMessage
    {
        get; init;
    }
    public DateTime ProcessedAtUtc { get; init; } = DateTime.UtcNow;
}
