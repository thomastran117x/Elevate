using backend.main.features.auth.contracts.responses;
using backend.main.shared.responses;

namespace backend.main.features.auth.history;

public interface ISecurityHistoryService
{
    Task<PagedResponse<AccountSecurityEventResponse>> GetPageAsync(
        int userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
