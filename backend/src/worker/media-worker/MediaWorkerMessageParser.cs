using System.Text.Json;

using backend.main.shared.providers;
using backend.main.shared.providers.messages;

namespace backend.worker.media_worker;

public sealed class MediaWorkerMessageParseException : Exception
{
    public MediaWorkerMessageParseException(string message)
        : base(message)
    {
    }

    public MediaWorkerMessageParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class MediaWorkerMessageParser
{
    public static MediaValidationRequestMessage Parse(MediaWorkerEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Payload))
            throw new MediaWorkerMessageParseException("Media validation payload is empty.");

        MediaValidationRequestMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<MediaValidationRequestMessage>(envelope.Payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new MediaWorkerMessageParseException("Media validation payload is not valid JSON.", ex);
        }

        if (message == null)
            throw new MediaWorkerMessageParseException("Media validation payload could not be deserialized.");

        if (message.MediaAssetId == Guid.Empty)
            throw new MediaWorkerMessageParseException("Media validation payload has no media asset id.");

        if (message.Attempt <= 0)
            throw new MediaWorkerMessageParseException("Media validation payload has no claim attempt.");

        if (string.IsNullOrWhiteSpace(message.QuarantineBlobPath))
            throw new MediaWorkerMessageParseException("Media validation payload has no quarantine blob path.");

        if (string.IsNullOrWhiteSpace(message.PublicUrl))
            throw new MediaWorkerMessageParseException("Media validation payload has no public URL.");

        if (string.IsNullOrWhiteSpace(message.Subject))
            throw new MediaWorkerMessageParseException("Media validation payload has no subject.");

        return message;
    }
}
