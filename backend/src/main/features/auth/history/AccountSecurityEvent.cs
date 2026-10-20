using backend.main.features.auth.events;

namespace backend.main.features.auth.history;

/// <summary>
/// One entry in an account owner's security history: a sign-in, recovery, factor, provider, or
/// session change, persisted from the auth security event contract by the security-history worker.
/// </summary>
/// <remarks>
/// Append only. Rows are inserted by the worker and removed only by the retention purge or by the
/// account's deletion cascade; nothing updates them. Every column is a closed classification, a
/// server-generated identifier, or a masked network prefix, so no credential, code, contact
/// address, or client-supplied text is ever stored.
/// </remarks>
public class AccountSecurityEvent
{
    public long Id
    {
        get; set;
    }

    /// <summary>Set by the producer; makes a redelivered Kafka message a no-op.</summary>
    public Guid MessageId
    {
        get; set;
    }

    public int UserId
    {
        get; set;
    }

    public AccountSecurityEventKind Kind
    {
        get; set;
    }

    /// <summary>The catalog name the entry was mapped from, e.g. <c>auth.sign_in.succeeded</c>.</summary>
    public required string EventName
    {
        get; set;
    }

    /// <summary>Snake-case <see cref="AuthSecurityMethod"/> wire name, when the event had one.</summary>
    public string? Method
    {
        get; set;
    }

    /// <summary>Snake-case <see cref="AuthSecurityFactor"/> wire name, when the event had one.</summary>
    public string? Factor
    {
        get; set;
    }

    /// <summary>Snake-case <see cref="AuthSecurityReason"/> wire name, when it adds meaning.</summary>
    public string? Reason
    {
        get; set;
    }

    public SecurityHistoryClient Client { get; set; } = SecurityHistoryClient.Unknown;

    public AuthDeviceClass DeviceClass { get; set; } = AuthDeviceClass.Unknown;

    /// <summary>The source address truncated to its network prefix (IPv4 /24, IPv6 /48).</summary>
    public string? Network
    {
        get; set;
    }

    /// <summary>The request id of the request that produced the event, for support lookups.</summary>
    public string? CorrelationId
    {
        get; set;
    }

    /// <summary>An administrator, not the account owner, caused the change.</summary>
    public bool InitiatedByAdmin
    {
        get; set;
    }

    public DateTime OccurredAtUtc
    {
        get; set;
    }

    public DateTime RecordedAtUtc
    {
        get; set;
    }

    /// <summary>
    /// When the retention purge may delete the row. Reads filter on it as well, so an entry past
    /// its window is never shown in the gap before the next purge.
    /// </summary>
    public DateTime ExpiresAtUtc
    {
        get; set;
    }
}
