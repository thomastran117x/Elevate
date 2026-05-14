using System.Text.Json;

using backend.main.features.clubs.search;
using backend.main.shared.providers;

namespace backend.worker.club_indexer;

public enum ClubIndexerOperation
{
    Upsert,
    Delete
}

public sealed record ClubIndexerMessage(
    ClubIndexerOperation Operation,
    ClubDocument? Document,
    int? ClubId)
{
    public static ClubIndexerMessage Upsert(ClubDocument document) =>
        new(ClubIndexerOperation.Upsert, document, document.Id);

    public static ClubIndexerMessage Delete(int clubId) =>
        new(ClubIndexerOperation.Delete, null, clubId);
}

public sealed class ClubIndexerMessageParseException : Exception
{
    public ClubIndexerMessageParseException(string message)
        : base(message)
    {
    }
}

public static class ClubIndexerMessageParser
{
    public static ClubIndexerMessage Parse(ClubIndexerEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Operation))
            throw new ClubIndexerMessageParseException("CDC eventType header is required.");

        if (string.IsNullOrWhiteSpace(envelope.Payload))
            throw new ClubIndexerMessageParseException("CDC payload body is required.");

        var operation = envelope.Operation.Trim().ToLowerInvariant();
        return operation switch
        {
            "upsert" => ClubIndexerMessage.Upsert(ParseDocument(envelope.Payload)),
            "delete" => ClubIndexerMessage.Delete(ParseDeletePayload(envelope.Payload).ClubId),
            _ => throw new ClubIndexerMessageParseException(
                $"CDC eventType '{envelope.Operation}' is not supported.")
        };
    }

    private static ClubDocument ParseDocument(string payload)
    {
        ClubDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ClubDocument>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new ClubIndexerMessageParseException(
                $"Invalid upsert payload JSON: {ex.Message}");
        }

        if (document == null)
            throw new ClubIndexerMessageParseException("Upsert payload could not be deserialized.");

        ValidateDocument(document);
        return document;
    }

    private static ClubSearchDeletePayload ParseDeletePayload(string payload)
    {
        ClubSearchDeletePayload? deletePayload;

        try
        {
            deletePayload = JsonSerializer.Deserialize<ClubSearchDeletePayload>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new ClubIndexerMessageParseException(
                $"Invalid delete payload JSON: {ex.Message}");
        }

        if (deletePayload == null)
            throw new ClubIndexerMessageParseException("Delete payload could not be deserialized.");

        if (deletePayload.ClubId <= 0)
            throw new ClubIndexerMessageParseException("Delete payload clubId must be a positive integer.");

        return deletePayload;
    }

    private static void ValidateDocument(ClubDocument document)
    {
        if (document.Id <= 0)
            throw new ClubIndexerMessageParseException("Club document id must be a positive integer.");

        if (string.IsNullOrWhiteSpace(document.Name))
            throw new ClubIndexerMessageParseException("Club document name is required.");

        if (string.IsNullOrWhiteSpace(document.Description))
            throw new ClubIndexerMessageParseException("Club document description is required.");

        if (string.IsNullOrWhiteSpace(document.ClubType))
            throw new ClubIndexerMessageParseException("Club document clubType is required.");

        if (document.MemberCount < 0)
            throw new ClubIndexerMessageParseException("Club document memberCount cannot be negative.");

        if (document.Rating.HasValue && (document.Rating.Value < 0 || document.Rating.Value > 5))
            throw new ClubIndexerMessageParseException("Club document rating must be between 0 and 5.");

        if (document.CreatedAt == default)
            throw new ClubIndexerMessageParseException("Club document createdAt is required.");

        if (document.UpdatedAt == default)
            throw new ClubIndexerMessageParseException("Club document updatedAt is required.");

        if (document.UpdatedAt < document.CreatedAt)
            throw new ClubIndexerMessageParseException("Club document updatedAt cannot be earlier than createdAt.");
    }
}
