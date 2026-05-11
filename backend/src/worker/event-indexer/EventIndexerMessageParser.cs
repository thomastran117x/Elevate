using System.Text.Json;

using backend.main.dtos.messages;
using backend.main.models.documents;
using backend.main.shared.providers;

namespace backend.worker.event_indexer;

public enum EventIndexerOperation
{
    Upsert,
    Delete
}

public sealed record EventIndexerMessage(
    EventIndexerOperation Operation,
    EventDocument? Document,
    int? EventId)
{
    public static EventIndexerMessage Upsert(EventDocument document) =>
        new(EventIndexerOperation.Upsert, document, document.Id);

    public static EventIndexerMessage Delete(int eventId) =>
        new(EventIndexerOperation.Delete, null, eventId);
}

public sealed class EventIndexerMessageParseException : Exception
{
    public EventIndexerMessageParseException(string message)
        : base(message)
    {
    }
}

public static class EventIndexerMessageParser
{
    public static EventIndexerMessage Parse(EventIndexerEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Operation))
            throw new EventIndexerMessageParseException("CDC eventType header is required.");

        if (string.IsNullOrWhiteSpace(envelope.Payload))
            throw new EventIndexerMessageParseException("CDC payload body is required.");

        var operation = envelope.Operation.Trim().ToLowerInvariant();
        return operation switch
        {
            "upsert" => EventIndexerMessage.Upsert(ParseDocument(envelope.Payload)),
            "delete" => EventIndexerMessage.Delete(ParseDeletePayload(envelope.Payload).EventId),
            _ => throw new EventIndexerMessageParseException(
                $"CDC eventType '{envelope.Operation}' is not supported.")
        };
    }

    private static EventDocument ParseDocument(string payload)
    {
        EventDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<EventDocument>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new EventIndexerMessageParseException(
                $"Invalid upsert payload JSON: {ex.Message}");
        }

        if (document == null)
            throw new EventIndexerMessageParseException("Upsert payload could not be deserialized.");

        ValidateDocument(document);
        return document;
    }

    private static EventSearchDeletePayload ParseDeletePayload(string payload)
    {
        EventSearchDeletePayload? deletePayload;

        try
        {
            deletePayload = JsonSerializer.Deserialize<EventSearchDeletePayload>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new EventIndexerMessageParseException(
                $"Invalid delete payload JSON: {ex.Message}");
        }

        if (deletePayload == null)
            throw new EventIndexerMessageParseException("Delete payload could not be deserialized.");

        if (deletePayload.EventId <= 0)
            throw new EventIndexerMessageParseException("Delete payload eventId must be a positive integer.");

        return deletePayload;
    }

    private static void ValidateDocument(EventDocument document)
    {
        if (document.Id <= 0)
            throw new EventIndexerMessageParseException("Event document id must be a positive integer.");

        if (document.ClubId <= 0)
            throw new EventIndexerMessageParseException("Event document clubId must be a positive integer.");

        if (string.IsNullOrWhiteSpace(document.Name))
            throw new EventIndexerMessageParseException("Event document name is required.");

        if (string.IsNullOrWhiteSpace(document.Location))
            throw new EventIndexerMessageParseException("Event document location is required.");

        if (string.IsNullOrWhiteSpace(document.Category))
            throw new EventIndexerMessageParseException("Event document category is required.");

        if (document.StartTime == default)
            throw new EventIndexerMessageParseException("Event document startTime is required.");

        if (document.CreatedAt == default)
            throw new EventIndexerMessageParseException("Event document createdAt is required.");

        if (document.UpdatedAt == default)
            throw new EventIndexerMessageParseException("Event document updatedAt is required.");

        if (document.RegistrationCount < 0)
            throw new EventIndexerMessageParseException("Event document registrationCount cannot be negative.");

        if (document.EndTime.HasValue && document.EndTime.Value < document.StartTime)
            throw new EventIndexerMessageParseException("Event document endTime cannot be earlier than startTime.");

        if (document.UpdatedAt < document.CreatedAt)
            throw new EventIndexerMessageParseException("Event document updatedAt cannot be earlier than createdAt.");
    }
}
