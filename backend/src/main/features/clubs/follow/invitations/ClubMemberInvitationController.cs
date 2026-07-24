using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.clubs.follow.invitations.contracts.requests;
using backend.main.features.clubs.follow.invitations.contracts.responses;
using backend.main.shared.responses;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.follow.invitations
{
    /// <summary>
    /// Club member invitations. Organisers (owner or manager) invite existing users by
    /// username/email (Redis-backed, emailed) or mint shareable invite links (DB-backed, no email).
    /// Both funnel through the recipient-facing resolve/accept endpoints and grant membership.
    /// </summary>
    [ApiController]
    [FeatureGate(FeatureFlagKeys.Clubs)]
    [FeatureGate(FeatureFlagKeys.ClubsFollow)]
    [Route("clubs")]
    public sealed class ClubMemberInvitationController : ControllerBase
    {
        private readonly IClubMemberInvitationService _invitationService;

        public ClubMemberInvitationController(IClubMemberInvitationService invitationService)
        {
            _invitationService = invitationService;
        }

        // ---- Specific invites (owner/manager) ----

        [Authorize]
        [HttpPost("{clubId}/members/invitations")]
        [ProducesResponseType(typeof(ApiResponse<ClubMemberInvitationResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateInvitation(int clubId, [FromBody] CreateClubMemberInvitationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var invitation = await _invitationService.CreateInvitationAsync(
                    clubId,
                    user.Id,
                    user.Role,
                    request.Identifier);

                return StatusCode(201, new ApiResponse<ClubMemberInvitationResponse>(
                    $"Invitation sent to {invitation.RecipientEmail}.",
                    invitation));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpGet("{clubId}/members/invitations")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubMemberInvitationResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetInvitations(int clubId)
        {
            try
            {
                var user = User.GetUserPayload();
                var invitations = await _invitationService.GetClubInvitationsAsync(clubId, user.Id, user.Role);

                return Ok(new ApiResponse<IEnumerable<ClubMemberInvitationResponse>>(
                    "Pending member invitations fetched successfully.",
                    invitations));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("{clubId}/members/invitations/{recipientUserId}/revoke")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RevokeInvitation(int clubId, int recipientUserId)
        {
            try
            {
                var user = User.GetUserPayload();
                await _invitationService.RevokeInvitationAsync(clubId, recipientUserId, user.Id, user.Role);

                return Ok(new MessageResponse(
                    $"Invitation for user {recipientUserId} has been revoked."));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        // ---- Invite links (owner/manager) ----

        [Authorize]
        [HttpPost("{clubId}/members/invitation-links")]
        [ProducesResponseType(typeof(ApiResponse<ClubInvitationLinkResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateLink(int clubId, [FromBody] CreateClubMemberInviteLinkRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var link = await _invitationService.CreateLinkAsync(
                    clubId,
                    user.Id,
                    user.Role,
                    request.ExpiresAt,
                    request.MaxRedemptions);

                return StatusCode(201, new ApiResponse<ClubInvitationLinkResponse>(
                    "Invite link created successfully.",
                    link));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpGet("{clubId}/members/invitation-links")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubInvitationLinkResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLinks(int clubId)
        {
            try
            {
                var user = User.GetUserPayload();
                var links = await _invitationService.GetLinksAsync(clubId, user.Id, user.Role);

                return Ok(new ApiResponse<IEnumerable<ClubInvitationLinkResponse>>(
                    "Invite links fetched successfully.",
                    links));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("{clubId}/members/invitation-links/{linkId}/revoke")]
        [ProducesResponseType(typeof(ApiResponse<ClubInvitationLinkResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RevokeLink(int clubId, int linkId)
        {
            try
            {
                var user = User.GetUserPayload();
                var link = await _invitationService.RevokeLinkAsync(clubId, linkId, user.Id, user.Role);

                return Ok(new ApiResponse<ClubInvitationLinkResponse>(
                    $"Invite link {linkId} has been revoked.",
                    link));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        // ---- Recipient-facing (both sources) ----

        [HttpPost("members/invitations/resolve")]
        [ProducesResponseType(typeof(ApiResponse<ClubMemberInvitationResolveResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResolveInvitation([FromBody] ClubMemberInvitationTokenRequest request)
        {
            try
            {
                var user = GetOptionalUserPayload();
                var result = await _invitationService.ResolveAsync(request.Token, user?.Id);

                return Ok(new ApiResponse<ClubMemberInvitationResolveResponse>(
                    "Invitation resolved successfully.",
                    result));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("members/invitations/accept")]
        [ProducesResponseType(typeof(ApiResponse<ClubMemberInvitationDecisionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AcceptInvitation([FromBody] ClubMemberInvitationTokenRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var result = await _invitationService.AcceptAsync(request.Token, user.Id);

                return Ok(new ApiResponse<ClubMemberInvitationDecisionResponse>(
                    "Invitation accepted successfully.",
                    result));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("members/invitations/decline")]
        [ProducesResponseType(typeof(ApiResponse<ClubMemberInvitationDecisionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeclineInvitation([FromBody] ClubMemberInvitationTokenRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var result = await _invitationService.DeclineAsync(request.Token, user.Id);

                return Ok(new ApiResponse<ClubMemberInvitationDecisionResponse>(
                    "Invitation declined successfully.",
                    result));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("members/invitation-links/redeem")]
        [ProducesResponseType(typeof(ApiResponse<ClubMemberInvitationDecisionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> RedeemLink([FromBody] ClubMemberInvitationTokenRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var result = await _invitationService.RedeemLinkAsync(request.Token, user.Id);

                return Ok(new ApiResponse<ClubMemberInvitationDecisionResponse>(
                    "You have joined the club successfully.",
                    result));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        private UserIdentityPayload? GetOptionalUserPayload()
        {
            if (User.Identity?.IsAuthenticated != true)
                return null;

            return User.GetUserPayload();
        }
    }
}
