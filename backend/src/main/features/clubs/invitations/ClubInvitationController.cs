using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.clubs.invitations.contracts.requests;
using backend.main.features.clubs.invitations.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.clubs.invitations
{
    /// <summary>
    /// Club staff invitations by username/email. Pending invites live in Redis (TTL-based expiry);
    /// the invited user becomes staff only when they accept via the emailed, recipient-bound token.
    /// </summary>
    [ApiController]
    [FeatureGate(FeatureFlagKeys.Clubs)]
    [Route("clubs")]
    public sealed class ClubInvitationController : ControllerBase
    {
        private readonly IClubInvitationService _invitationService;

        public ClubInvitationController(IClubInvitationService invitationService)
        {
            _invitationService = invitationService;
        }

        [Authorize]
        [HttpPost("{clubId}/staff/invitations")]
        [ProducesResponseType(typeof(ApiResponse<ClubInvitationResponse>), StatusCodes.Status201Created)]
        public async Task<IActionResult> CreateInvitation(int clubId, [FromBody] CreateClubStaffInvitationRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var invitation = await _invitationService.CreateInvitationAsync(
                    clubId,
                    user.Id,
                    user.Role,
                    request.Identifier,
                    request.Role);

                return StatusCode(201, new ApiResponse<ClubInvitationResponse>(
                    $"Invitation sent to {invitation.RecipientEmail}.",
                    invitation));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpGet("{clubId}/staff/invitations")]
        [ProducesResponseType(typeof(ApiResponse<IEnumerable<ClubInvitationResponse>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetInvitations(int clubId)
        {
            try
            {
                var user = User.GetUserPayload();
                var invitations = await _invitationService.GetClubInvitationsAsync(clubId, user.Id, user.Role);

                return Ok(new ApiResponse<IEnumerable<ClubInvitationResponse>>(
                    "Pending invitations fetched successfully.",
                    invitations));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("{clubId}/staff/invitations/{recipientUserId}/revoke")]
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

        [HttpPost("invitations/resolve")]
        [ProducesResponseType(typeof(ApiResponse<ClubInvitationResolveResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResolveInvitation([FromBody] ClubInvitationTokenRequest request)
        {
            try
            {
                var user = GetOptionalUserPayload();
                var result = await _invitationService.ResolveInvitationAsync(request.Token, user?.Id);

                return Ok(new ApiResponse<ClubInvitationResolveResponse>(
                    "Invitation resolved successfully.",
                    result));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("invitations/accept")]
        [ProducesResponseType(typeof(ApiResponse<ClubInvitationDecisionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> AcceptInvitation([FromBody] ClubInvitationTokenRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var result = await _invitationService.AcceptInvitationAsync(request.Token, user.Id, user.Email);

                return Ok(new ApiResponse<ClubInvitationDecisionResponse>(
                    "Invitation accepted successfully.",
                    result));
            }
            catch (Exception ex)
            {
                return HandleError.Resolve(ex);
            }
        }

        [Authorize]
        [HttpPost("invitations/decline")]
        [ProducesResponseType(typeof(ApiResponse<ClubInvitationDecisionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeclineInvitation([FromBody] ClubInvitationTokenRequest request)
        {
            try
            {
                var user = User.GetUserPayload();
                var result = await _invitationService.DeclineInvitationAsync(request.Token, user.Id);

                return Ok(new ApiResponse<ClubInvitationDecisionResponse>(
                    "Invitation declined successfully.",
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
