using backend.main.features.clubs.invitations.contracts.responses;
using backend.main.features.clubs.staff;

namespace backend.main.features.clubs.invitations
{
    /// <summary>
    /// Redis-backed club staff invitations. An owner invites an existing user (by username or email);
    /// the user receives an emailed, recipient-bound token and only becomes staff when they accept.
    /// </summary>
    public interface IClubInvitationService
    {
        Task<ClubInvitationResponse> CreateInvitationAsync(int clubId, int actorUserId, string actorRole, string identifier, ClubStaffRole role);
        Task<IReadOnlyList<ClubInvitationResponse>> GetClubInvitationsAsync(int clubId, int actorUserId, string actorRole);
        Task RevokeInvitationAsync(int clubId, int recipientUserId, int actorUserId, string actorRole);
        Task<ClubInvitationResolveResponse> ResolveInvitationAsync(string token, int? userId);
        Task<ClubInvitationDecisionResponse> AcceptInvitationAsync(string token, int userId, string userEmail);
        Task<ClubInvitationDecisionResponse> DeclineInvitationAsync(string token, int userId);
    }
}
