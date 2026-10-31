using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.shared.responses;

namespace backend.main.features.auth.history;

public sealed class SecurityHistoryService : ISecurityHistoryService
{
    private readonly ISecurityHistoryRepository _repository;
    private readonly TimeProvider _timeProvider;

    public SecurityHistoryService(ISecurityHistoryRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResponse<AccountSecurityEventResponse>> GetPageAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var (items, totalCount) = await _repository.GetPageAsync(userId, now, page, pageSize, cancellationToken);

        return new PagedResponse<AccountSecurityEventResponse>(
            items.Select(ToResponse).ToList(),
            totalCount,
            page,
            pageSize);
    }

    internal static AccountSecurityEventResponse ToResponse(AccountSecurityEvent entry) => new()
    {
        Id = entry.Id,
        Kind = AuthSecurityWireNames.Of(entry.Kind),
        OccurredAt = entry.OccurredAtUtc,
        Method = entry.Method,
        Factor = entry.Factor,
        Reason = entry.Reason,
        Client = AuthSecurityWireNames.Of(entry.Client),
        DeviceClass = AuthSecurityWireNames.Of(entry.DeviceClass),
        Network = entry.Network,
        InitiatedByAdmin = entry.InitiatedByAdmin,
        // An administrator's action carries the administrator's request id. It stays stored for
        // investigations but is not shown to the owner, any more than the rest of that request.
        Reference = entry.InitiatedByAdmin ? null : entry.CorrelationId,
    };
}
