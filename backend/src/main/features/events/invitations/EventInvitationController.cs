using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.events.invitations.contracts.requests;
using backend.main.features.events.invitations.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.events.invitations;

/// <summary>
/// Invitation and invite-link flows for private or managed events.
/// </summary>
[ApiController]
[FeatureGate(FeatureFlagKeys.EventsInvitations)]
[Route("events")]
public sealed class EventInvitationController : ControllerBase
{
    private readonly IEventInvitationService _invitationService;

    public EventInvitationController(IEventInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    [Authorize]
    [HttpPost("{eventId}/invitations")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventInvitationResponse>>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateInvitations(int eventId, [FromBody] CreateEventInvitationsRequest request)
    {
        try
        {
            var user = User.GetUserPayload();
            var invitations = await _invitationService.CreateInvitationsAsync(
                eventId,
                user.Id,
                user.Role,
                request.UserIds ?? [],
                request.Emails ?? [],
                request.ExpiresAt);

            return StatusCode(201, new ApiResponse<IEnumerable<EventInvitationResponse>>(
                $"{invitations.Count} invitation(s) prepared successfully.",
                invitations));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpGet("{eventId}/invitations")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventInvitationResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvitations(int eventId)
    {
        try
        {
            var user = User.GetUserPayload();
            var invitations = await _invitationService.GetEventInvitationsAsync(eventId, user.Id, user.Role);

            return Ok(new ApiResponse<IEnumerable<EventInvitationResponse>>(
                "Invitations fetched successfully.",
                invitations));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("{eventId}/invitations/{invitationId}/revoke")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvitation(int eventId, int invitationId)
    {
        try
        {
            var user = User.GetUserPayload();
            var invitation = await _invitationService.RevokeInvitationAsync(eventId, invitationId, user.Id, user.Role);

            return Ok(new ApiResponse<EventInvitationResponse>(
                $"Invitation {invitationId} revoked successfully.",
                invitation));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("{eventId}/invitation-links")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationLinkResponse>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateInvitationLink(int eventId, [FromBody] CreateEventInvitationLinkRequest request)
    {
        try
        {
            var user = User.GetUserPayload();
            var link = await _invitationService.CreateInvitationLinkAsync(
                eventId,
                user.Id,
                user.Role,
                request.MaxRedemptions,
                request.ExpiresAt);

            return StatusCode(201, new ApiResponse<EventInvitationLinkResponse>(
                "Invitation link created successfully.",
                link));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpGet("{eventId}/invitation-links")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventInvitationLinkResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvitationLinks(int eventId)
    {
        try
        {
            var user = User.GetUserPayload();
            var links = await _invitationService.GetInvitationLinksAsync(eventId, user.Id, user.Role);

            return Ok(new ApiResponse<IEnumerable<EventInvitationLinkResponse>>(
                "Invitation links fetched successfully.",
                links));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("{eventId}/invitation-links/{linkId}/revoke")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationLinkResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvitationLink(int eventId, int linkId)
    {
        try
        {
            var user = User.GetUserPayload();
            var link = await _invitationService.RevokeInvitationLinkAsync(eventId, linkId, user.Id, user.Role);

            return Ok(new ApiResponse<EventInvitationLinkResponse>(
                $"Invitation link {linkId} revoked successfully.",
                link));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [HttpPost("invitations/resolve")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationResolveResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveInvitation([FromBody] ResolveEventInvitationRequest request)
    {
        try
        {
            var user = GetOptionalUserPayload();
            var result = await _invitationService.ResolveInvitationAsync(request.Token, user?.Id, user?.Email);

            return Ok(new ApiResponse<EventInvitationResolveResponse>(
                "Invitation resolved successfully.",
                result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("invitations/accept")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationDecisionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitation([FromBody] DecideEventInvitationRequest request)
    {
        try
        {
            var user = User.GetUserPayload();
            var result = await _invitationService.AcceptInvitationAsync(request.Token, user.Id, user.Email);

            return Ok(new ApiResponse<EventInvitationDecisionResponse>(
                "Invitation accepted successfully.",
                result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("invitations/{invitationId}/accept")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationDecisionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvitationById(int invitationId)
    {
        try
        {
            var user = User.GetUserPayload();
            var result = await _invitationService.AcceptInvitationByIdAsync(invitationId, user.Id, user.Email);

            return Ok(new ApiResponse<EventInvitationDecisionResponse>(
                "Invitation accepted successfully.",
                result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("invitations/decline")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationDecisionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclineInvitation([FromBody] DecideEventInvitationRequest request)
    {
        try
        {
            var user = User.GetUserPayload();
            var result = await _invitationService.DeclineInvitationAsync(request.Token, user.Id, user.Email);

            return Ok(new ApiResponse<EventInvitationDecisionResponse>(
                "Invitation declined successfully.",
                result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpPost("invitations/{invitationId}/decline")]
    [ProducesResponseType(typeof(ApiResponse<EventInvitationDecisionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclineInvitationById(int invitationId)
    {
        try
        {
            var user = User.GetUserPayload();
            var result = await _invitationService.DeclineInvitationByIdAsync(invitationId, user.Id, user.Email);

            return Ok(new ApiResponse<EventInvitationDecisionResponse>(
                "Invitation declined successfully.",
                result));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

            return HandleError.Resolve(ex);
        }
    }

    [Authorize]
    [HttpGet("me/invited")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<EventInvitationResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyInvitations()
    {
        try
        {
            var user = User.GetUserPayload();
            var invitations = await _invitationService.GetMyInvitationsAsync(user.Id, user.Email);

            return Ok(new ApiResponse<IEnumerable<EventInvitationResponse>>(
                "Your invitations have been fetched successfully.",
                invitations));
        }
        catch (Exception ex)
        {
            if (ex is AppException)
                return HandleError.Resolve(ex);

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




