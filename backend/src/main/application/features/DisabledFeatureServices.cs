using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.posts;
using backend.main.features.clubs.posts.search;
using backend.main.features.clubs.search;
using backend.main.features.events;
using backend.main.features.events.invitations;
using backend.main.features.events.invitations.contracts.responses;
using backend.main.features.events.registration;
using backend.main.features.events.registration.contracts.requests;
using backend.main.features.events.registration.contracts.responses;
using backend.main.features.events.search;
using backend.main.features.payment;
using backend.main.infrastructure.elasticsearch;
using backend.main.shared.exceptions.http;

namespace backend.main.application.features;

internal static class DisabledFeatureErrors
{
    public static NotAvailableException Create(string featureKey) =>
        new($"The '{featureKey}' feature is disabled.");
}

public sealed class DisabledFollowService : IFollowService
{
    public Task<FollowClub> GetFollowAsync(int id) => Task.FromException<FollowClub>(DisabledFeatureErrors.Create(FeatureFlagKeys.ClubsFollow));
    public Task<IEnumerable<FollowClub>> GetFollowsAsync(int page = 1, int pageSize = 20) => Task.FromException<IEnumerable<FollowClub>>(DisabledFeatureErrors.Create(FeatureFlagKeys.ClubsFollow));
    public Task<IEnumerable<FollowClub>> GetFollowsByUserAsync(int userId, int page = 1, int pageSize = 20) => Task.FromException<IEnumerable<FollowClub>>(DisabledFeatureErrors.Create(FeatureFlagKeys.ClubsFollow));
    public Task<IEnumerable<FollowClub>> GetFollowsByClubAsync(int clubId, int page = 1, int pageSize = 20) => Task.FromException<IEnumerable<FollowClub>>(DisabledFeatureErrors.Create(FeatureFlagKeys.ClubsFollow));
    public Task<bool> IsMemberAsync(int clubId, int userId) => Task.FromResult(false);
    public Task AddMembershipAsync(int clubId, int userId) => Task.FromException(DisabledFeatureErrors.Create(FeatureFlagKeys.ClubsFollow));
    public Task RemoveMembershipAsync(int clubId, int userId) => Task.FromException(DisabledFeatureErrors.Create(FeatureFlagKeys.ClubsFollow));
}

public sealed class DisabledEventInvitationService : IEventInvitationService
{
    public Task<bool> HasAcceptedInvitationAccessAsync(int eventId, int userId) => Task.FromResult(false);
    public Task<IReadOnlyList<EventInvitationResponse>> CreateInvitationsAsync(int eventId, int actorUserId, string actorRole, IEnumerable<int> userIds, IEnumerable<string> emails, DateTime? expiresAt) => Task.FromException<IReadOnlyList<EventInvitationResponse>>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<IReadOnlyList<EventInvitationResponse>> GetEventInvitationsAsync(int eventId, int actorUserId, string actorRole) => Task.FromException<IReadOnlyList<EventInvitationResponse>>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationResponse> RevokeInvitationAsync(int eventId, int invitationId, int actorUserId, string actorRole) => Task.FromException<EventInvitationResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationLinkResponse> CreateInvitationLinkAsync(int eventId, int actorUserId, string actorRole, int maxRedemptions, DateTime expiresAt) => Task.FromException<EventInvitationLinkResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<IReadOnlyList<EventInvitationLinkResponse>> GetInvitationLinksAsync(int eventId, int actorUserId, string actorRole) => Task.FromException<IReadOnlyList<EventInvitationLinkResponse>>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationLinkResponse> RevokeInvitationLinkAsync(int eventId, int linkId, int actorUserId, string actorRole) => Task.FromException<EventInvitationLinkResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationResolveResponse> ResolveInvitationAsync(string token, int? userId = null, string? email = null) => Task.FromException<EventInvitationResolveResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationDecisionResponse> AcceptInvitationAsync(string token, int userId, string userEmail) => Task.FromException<EventInvitationDecisionResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationDecisionResponse> DeclineInvitationAsync(string token, int userId, string userEmail) => Task.FromException<EventInvitationDecisionResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationDecisionResponse> AcceptInvitationByIdAsync(int invitationId, int userId, string userEmail) => Task.FromException<EventInvitationDecisionResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<EventInvitationDecisionResponse> DeclineInvitationByIdAsync(int invitationId, int userId, string userEmail) => Task.FromException<EventInvitationDecisionResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task<IReadOnlyList<EventInvitationResponse>> GetMyInvitationsAsync(int userId, string userEmail) => Task.FromException<IReadOnlyList<EventInvitationResponse>>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
    public Task MarkInvitationDeliveryStatusAsync(int invitationId, EventInvitationDeliveryStatus status, string? errorMessage) => Task.FromException(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsInvitations));
}

public sealed class DisabledEventRegistrationService : IEventRegistrationService
{
    public Task RegisterAsync(int eventId, int userId, string userRole, RegisterEventRequest? request = null) => Task.FromException(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task UnregisterAsync(int eventId, int userId, string userRole) => Task.FromException(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<EventRegistration> UpdateRegistrationAsync(int eventId, int userId, string userRole, UpdateRegistrationRequest request) => Task.FromException<EventRegistration>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<bool> IsRegisteredAsync(int eventId, int userId, string userRole) => Task.FromException<bool>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<EventRegistration?> GetMyRegistrationAsync(int eventId, int userId, string userRole) => Task.FromException<EventRegistration?>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<IEnumerable<EventRegistration>> GetRegistrationsByEventAsync(int eventId, int page = 1, int pageSize = 20) => Task.FromException<IEnumerable<EventRegistration>>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<IEnumerable<EventRegistration>> GetRegistrationsByUserAsync(int userId, int page = 1, int pageSize = 20) => Task.FromException<IEnumerable<EventRegistration>>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<BatchRegistrationResultResponse> BatchRegisterAsync(int userId, string userRole, IEnumerable<int> eventIds) => Task.FromException<BatchRegistrationResultResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
    public Task<BatchRegistrationResultResponse> BatchUnregisterAsync(int userId, string userRole, IEnumerable<int> eventIds) => Task.FromException<BatchRegistrationResultResponse>(DisabledFeatureErrors.Create(FeatureFlagKeys.EventsRegistration));
}

public sealed class DisabledPaymentService : IPaymentService
{
    public Task<Payment> CreatePaymentSession(int userId, string userRole, int eventId, string? idempotencyKey = null) => Task.FromException<Payment>(DisabledFeatureErrors.Create(FeatureFlagKeys.Payment));
    public Task<Payment> GetPayment(int paymentId) => Task.FromException<Payment>(DisabledFeatureErrors.Create(FeatureFlagKeys.Payment));
    public Task<List<Payment>> GetPaymentsByUser(int userId, int page = 1, int pageSize = 20) => Task.FromException<List<Payment>>(DisabledFeatureErrors.Create(FeatureFlagKeys.Payment));
    public Task HandleWebhook(string payload, string signature) => Task.FromException(DisabledFeatureErrors.Create(FeatureFlagKeys.Payment));
    public Task<Payment> RefundPayment(int paymentId, int requestingUserId) => Task.FromException<Payment>(DisabledFeatureErrors.Create(FeatureFlagKeys.Payment));
}

public sealed class DisabledEventSearchService : IEventSearchService
{
    public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task IndexAsync(EventDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(int eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task BulkIndexAsync(IEnumerable<EventDocument> documents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<EventSearchResult> SearchAsync(EventSearchCriteria criteria) => Task.FromException<EventSearchResult>(new ElasticsearchDisabledException("Event search is disabled by feature flag."));
}

public sealed class DisabledClubSearchService : IClubSearchService
{
    public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task IndexAsync(ClubDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(int clubId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task BulkIndexAsync(IEnumerable<ClubDocument> documents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<ClubSearchResult> SearchAsync(ClubSearchCriteria criteria) => Task.FromException<ClubSearchResult>(new ElasticsearchDisabledException("Club search is disabled by feature flag."));
}

public sealed class DisabledClubPostSearchService : IClubPostSearchService
{
    public Task EnsureIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task IndexAsync(ClubPostDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(int postId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task BulkIndexAsync(IEnumerable<ClubPostDocument> documents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<(List<int> Ids, int TotalCount)> SearchByClubAsync(int clubId, string search, PostSortBy sortBy, int page, int pageSize) => Task.FromException<(List<int>, int)>(new ElasticsearchDisabledException("Club post search is disabled by feature flag."));
    public Task<(List<int> Ids, int TotalCount)> SearchAllAsync(string search, PostSortBy sortBy, int page, int pageSize) => Task.FromException<(List<int>, int)>(new ElasticsearchDisabledException("Club post search is disabled by feature flag."));
}

public sealed class DisabledEventSearchOutboxWriter : IEventSearchOutboxWriter
{
    public void StageUpsert(Events ev)
    {
    }
    public void StageSync(Events ev)
    {
    }
    public void StageDelete(int eventId)
    {
    }
}

public sealed class DisabledClubSearchOutboxWriter : IClubSearchOutboxWriter
{
    public void StageUpsert(Club club)
    {
    }
    public void StageDelete(int clubId)
    {
    }
}

public sealed class DisabledClubPostSearchOutboxWriter : IClubPostSearchOutboxWriter
{
    public void StageUpsert(ClubPost post)
    {
    }
    public void StageDelete(int postId)
    {
    }
}

public sealed class DisabledEventReindexService : IEventReindexService
{
    public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<int>(DisabledFeatureErrors.Create(FeatureFlagKeys.SearchReindex));
}

public sealed class DisabledClubReindexService : IClubReindexService
{
    public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<int>(DisabledFeatureErrors.Create(FeatureFlagKeys.SearchReindex));
}

public sealed class DisabledClubPostReindexService : IClubPostReindexService
{
    public Task<int> ReindexAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<int>(DisabledFeatureErrors.Create(FeatureFlagKeys.SearchReindex));
}


