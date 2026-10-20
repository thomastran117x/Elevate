using backend.main.features.auth.events;
using backend.main.features.auth.history;

namespace backend.main.shared.providers.messages
{
    /// <summary>
    /// One account security history entry on its way from the API to the security-history worker.
    /// </summary>
    /// <remarks>
    /// Carries only closed classifications, ids the server generated, and a masked network prefix.
    /// It deliberately has no email, username, phone, hash, session id, token, code, or raw
    /// User-Agent; a unit test pins the property set so none is added by accident. Enums travel by
    /// name, so the worker rejects a value it does not know instead of misreading it.
    /// </remarks>
    public sealed class SecurityHistoryMessage
    {
        /// <summary>Unique per event; the worker uses it to make a redelivery a no-op.</summary>
        public required Guid MessageId
        {
            get; init;
        }
        public required int UserId
        {
            get; init;
        }
        public required AccountSecurityEventKind Kind
        {
            get; init;
        }
        /// <summary>The catalog name of the source event, e.g. <c>auth.sign_in.succeeded</c>.</summary>
        public required string EventName
        {
            get; init;
        }
        public string? Method
        {
            get; init;
        }
        public string? Factor
        {
            get; init;
        }
        public string? Reason
        {
            get; init;
        }
        public SecurityHistoryClient Client { get; init; } = SecurityHistoryClient.Unknown;
        public AuthDeviceClass DeviceClass { get; init; } = AuthDeviceClass.Unknown;
        public string? Network
        {
            get; init;
        }
        public string? CorrelationId
        {
            get; init;
        }
        public bool InitiatedByAdmin
        {
            get; init;
        }
        public required DateTimeOffset OccurredAt
        {
            get; init;
        }
    }
}
