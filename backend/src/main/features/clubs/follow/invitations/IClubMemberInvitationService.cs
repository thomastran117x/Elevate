using backend.main.features.clubs.follow.invitations.contracts.responses;

namespace backend.main.features.clubs.follow.invitations
{
    /// <summary>
    /// Club member (follower) invitations. Two mechanisms funnel through one accept page:
    /// <list type="bullet">
    /// <item>A recipient-bound <b>specific invite</b> (Redis-backed, emailed) issued by username/email.</item>
    /// <item>A shareable <b>invite link</b> (DB-backed, no email) that anyone may redeem until it expires,
    /// is revoked, or hits its optional redemption cap.</item>
    /// </list>
    /// Either can be created by the club owner or a manager, and both grant membership on acceptance.
    /// </summary>
    public interface IClubMemberInvitationService
    {
        // Specific invites (owner/manager)
        Task<ClubMemberInvitationResponse> CreateInvitationAsync(int clubId, int actorUserId, string actorRole, string identifier);
        Task<IReadOnlyList<ClubMemberInvitationResponse>> GetClubInvitationsAsync(int clubId, int actorUserId, string actorRole);
        Task RevokeInvitationAsync(int clubId, int recipientUserId, int actorUserId, string actorRole);

        // Invite links (owner/manager)
        Task<ClubInvitationLinkResponse> CreateLinkAsync(int clubId, int actorUserId, string actorRole, DateTime expiresAt, int? maxRedemptions);
        Task<IReadOnlyList<ClubInvitationLinkResponse>> GetLinksAsync(int clubId, int actorUserId, string actorRole);
        Task<ClubInvitationLinkResponse> RevokeLinkAsync(int clubId, int linkId, int actorUserId, string actorRole);

        // Recipient-facing (works for both sources)
        Task<ClubMemberInvitationResolveResponse> ResolveAsync(string token, int? userId);
        Task<ClubMemberInvitationDecisionResponse> AcceptAsync(string token, int userId);
        Task<ClubMemberInvitationDecisionResponse> DeclineAsync(string token, int userId);
        Task<ClubMemberInvitationDecisionResponse> RedeemLinkAsync(string token, int userId);
    }
}
