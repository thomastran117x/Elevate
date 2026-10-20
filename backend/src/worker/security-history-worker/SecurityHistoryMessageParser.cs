using System.Net;
using System.Text.Json;

using backend.main.application.handlers;
using backend.main.features.auth.events;
using backend.main.features.auth.history;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

namespace backend.worker.security_history_worker;

/// <summary>A payload that can never become a valid history entry, however often it is retried.</summary>
public sealed class InvalidSecurityHistoryMessageException(string message) : Exception(message);

/// <summary>
/// Reads a security history message and holds it to the producer's contract before anything is
/// stored: only known classifications, a masked network prefix, and a well-formed request id.
/// </summary>
/// <remarks>
/// The topic is internal, but the table is shown to account owners for 90 days, so the worker does
/// not take the producer's word for it. A payload that fails here goes to the DLQ.
/// </remarks>
public static class SecurityHistoryMessageParser
{
    private static readonly HashSet<string> RecordedEventNames =
        SecurityHistoryCatalog.Recorded.Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> MethodNames = WireNames<AuthSecurityMethod>();
    private static readonly HashSet<string> FactorNames = WireNames<AuthSecurityFactor>();
    private static readonly HashSet<string> ReasonNames = WireNames<AuthSecurityReason>();

    public static SecurityHistoryMessage Parse(string payload)
    {
        SecurityHistoryMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SecurityHistoryMessage>(payload, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new InvalidSecurityHistoryMessageException($"Payload is not a security history message ({ex.GetType().Name}).");
        }

        if (message is null)
            throw new InvalidSecurityHistoryMessageException("Payload is empty.");

        Validate(message);
        return message;
    }

    private static void Validate(SecurityHistoryMessage message)
    {
        Require(message.MessageId != Guid.Empty, "MessageId is missing.");
        Require(message.UserId > 0, "UserId must be positive.");
        // Enums accept integers by default, so an out-of-range number would otherwise get through.
        Require(Enum.IsDefined(message.Kind), "Kind is not a known kind.");
        Require(Enum.IsDefined(message.Client), "Client is not a known client.");
        Require(Enum.IsDefined(message.DeviceClass), "DeviceClass is not a known device class.");
        Require(RecordedEventNames.Contains(message.EventName ?? string.Empty), "EventName is not a recorded event.");
        Require(message.Method is null || MethodNames.Contains(message.Method), "Method is not a known method.");
        Require(message.Factor is null || FactorNames.Contains(message.Factor), "Factor is not a known factor.");
        Require(message.Reason is null || ReasonNames.Contains(message.Reason), "Reason is not a known reason.");
        Require(message.Network is null || IsMaskedNetwork(message.Network), "Network is not a masked prefix.");
        Require(
            message.CorrelationId is null || RequestIdHandler.IsValidRequestId(message.CorrelationId),
            "CorrelationId is not a valid request id.");
        Require(message.OccurredAt != default, "OccurredAt is missing.");
    }

    /// <summary>Only exactly what <see cref="NetworkAddressMasker"/> produces, never a full address.</summary>
    private static bool IsMaskedNetwork(string network)
    {
        var slash = network.IndexOf('/');
        return slash > 0
            && IPAddress.TryParse(network[..slash], out var address)
            && NetworkAddressMasker.Mask(address) == network;
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
            throw new InvalidSecurityHistoryMessageException(error);
    }

    private static HashSet<string> WireNames<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => AuthSecurityWireNames.Of(value)).ToHashSet(StringComparer.Ordinal);
}
