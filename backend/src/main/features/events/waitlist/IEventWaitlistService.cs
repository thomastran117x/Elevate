using backend.main.features.events.waitlist.contracts.requests;
using backend.main.features.events.waitlist.contracts.responses;

namespace backend.main.features.events.waitlist
{
    public interface IEventWaitlistService
    {
        Task<EventWaitlistEntryResponse> JoinAsync(int eventId, int userId, string userRole, JoinWaitlistRequest? request = null);

        Task LeaveAsync(int eventId, int userId, string userRole);

        Task<MyWaitlistStatusResponse> GetMyStatusAsync(int eventId, int userId, string userRole);

        Task<(IReadOnlyList<EventWaitlistEntryResponse> Entries, int TotalCount)> GetEventWaitlistAsync(
            int eventId, int actorUserId, string actorRole, int page = 1, int pageSize = 20);

        Task RemoveEntryAsync(int eventId, int entryId, int actorUserId, string actorRole);

        Task<IReadOnlyList<WaitlistedEventResponse>> GetMyWaitlistsAsync(int userId, string userRole);

        Task<WaitlistPromotionResultResponse> PromoteNextAsync(int eventId, int actorUserId, string actorRole);
    }
}
