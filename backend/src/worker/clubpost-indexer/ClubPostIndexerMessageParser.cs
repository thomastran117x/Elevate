using System.Text.Json;

using backend.main.features.clubs.posts.search;
using backend.main.shared.providers;

namespace backend.worker.clubpost_indexer;

public enum ClubPostIndexerOperation
{
    Upsert,
    Delete
}

public sealed record ClubPostIndexerMessage(
    ClubPostIndexerOperation Operation,
    ClubPostDocument? Document,
    int? PostId)
{
    public static ClubPostIndexerMessage Upsert(ClubPostDocument document) =>
        new(ClubPostIndexerOperation.Upsert, document, document.Id);

    public static ClubPostIndexerMessage Delete(int postId) =>
        new(ClubPostIndexerOperation.Delete, null, postId);
}

public sealed class ClubPostIndexerMessageParseException : Exception
{
    public ClubPostIndexerMessageParseException(string message)
        : base(message)
    {
    }
}

public static class ClubPostIndexerMessageParser
{
    public static ClubPostIndexerMessage Parse(ClubPostIndexerEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Operation))
            throw new ClubPostIndexerMessageParseException("CDC eventType header is required.");

        if (string.IsNullOrWhiteSpace(envelope.Payload))
            throw new ClubPostIndexerMessageParseException("CDC payload body is required.");

        var operation = envelope.Operation.Trim().ToLowerInvariant();
        return operation switch
        {
            "upsert" => ClubPostIndexerMessage.Upsert(ParseDocument(envelope.Payload)),
            "delete" => ClubPostIndexerMessage.Delete(ParseDeletePayload(envelope.Payload).PostId),
            _ => throw new ClubPostIndexerMessageParseException(
                $"CDC eventType '{envelope.Operation}' is not supported.")
        };
    }

    private static ClubPostDocument ParseDocument(string payload)
    {
        ClubPostDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<ClubPostDocument>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new ClubPostIndexerMessageParseException(
                $"Invalid upsert payload JSON: {ex.Message}");
        }

        if (document == null)
            throw new ClubPostIndexerMessageParseException("Upsert payload could not be deserialized.");

        ValidateDocument(document);
        return document;
    }

    private static ClubPostSearchDeletePayload ParseDeletePayload(string payload)
    {
        ClubPostSearchDeletePayload? deletePayload;

        try
        {
            deletePayload = JsonSerializer.Deserialize<ClubPostSearchDeletePayload>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new ClubPostIndexerMessageParseException(
                $"Invalid delete payload JSON: {ex.Message}");
        }

        if (deletePayload == null)
            throw new ClubPostIndexerMessageParseException("Delete payload could not be deserialized.");

        if (deletePayload.PostId <= 0)
            throw new ClubPostIndexerMessageParseException("Delete payload postId must be a positive integer.");

        return deletePayload;
    }

    private static void ValidateDocument(ClubPostDocument document)
    {
        if (document.Id <= 0)
            throw new ClubPostIndexerMessageParseException("Club post document id must be a positive integer.");

        if (document.ClubId <= 0)
            throw new ClubPostIndexerMessageParseException("Club post document clubId must be a positive integer.");

        if (document.UserId <= 0)
            throw new ClubPostIndexerMessageParseException("Club post document userId must be a positive integer.");

        if (string.IsNullOrWhiteSpace(document.Title))
            throw new ClubPostIndexerMessageParseException("Club post document title is required.");

        if (string.IsNullOrWhiteSpace(document.PostType))
            throw new ClubPostIndexerMessageParseException("Club post document postType is required.");

        if (document.LikesCount < 0)
            throw new ClubPostIndexerMessageParseException("Club post document likesCount cannot be negative.");

        if (document.CreatedAt == default)
            throw new ClubPostIndexerMessageParseException("Club post document createdAt is required.");

        if (document.UpdatedAt == default)
            throw new ClubPostIndexerMessageParseException("Club post document updatedAt is required.");

        if (document.UpdatedAt < document.CreatedAt)
            throw new ClubPostIndexerMessageParseException("Club post document updatedAt cannot be earlier than createdAt.");
    }
}
