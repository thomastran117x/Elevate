using System.Security.Claims;

using backend.main.features.events.invitations;
using backend.main.features.events.invitations.contracts.requests;
using backend.main.features.events.invitations.contracts.responses;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Events;

public class EventInvitationControllerTests
{
    [Fact]
    public async Task CreateInvitations_ShouldReturnCreatedInvitationPayload()
    {
        var service = new Mock<IEventInvitationService>();
        service.Setup(s => s.CreateInvitationsAsync(9, 7, "Organizer", It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<string>>(), null))
            .ReturnsAsync(
            [
                new EventInvitationResponse
                {
                    Id = 4,
                    EventId = 9,
                    RecipientUserId = 55,
                    SourceType = "Direct",
                    LifecycleStatus = "Pending"
                }
            ]);

        var controller = CreateController(service.Object);

        var result = await controller.CreateInvitations(9, new CreateEventInvitationsRequest
        {
            UserIds = [55]
        });

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        var response = created.Value.Should().BeOfType<ApiResponse<IEnumerable<EventInvitationResponse>>>().Subject;
        response.Data.Should().ContainSingle(entry => entry.RecipientUserId == 55);
    }

    [Fact]
    public async Task ResolveInvitation_ShouldAllowAnonymousResolution()
    {
        var service = new Mock<IEventInvitationService>();
        service.Setup(s => s.ResolveInvitationAsync("invite-token", null, null))
            .ReturnsAsync(new EventInvitationResolveResponse
            {
                State = "pending",
                RequiresAuthentication = true,
                CanAccept = false,
                CanDecline = false,
                SourceType = "Direct"
            });

        var controller = new EventInvitationController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.ResolveInvitation(new ResolveEventInvitationRequest
        {
            Token = "invite-token"
        });

        var ok = result.Should().BeAssignableTo<ObjectResult>().Subject;
        ok.StatusCode.Should().Be(200);
        var response = ok.Value.Should().BeOfType<ApiResponse<EventInvitationResolveResponse>>().Subject;
        response.Data!.State.Should().Be("pending");
        response.Data.RequiresAuthentication.Should().BeTrue();
    }

    [Fact]
    public async Task AcceptInvitationById_ShouldReturnDecisionResponse()
    {
        var service = new Mock<IEventInvitationService>();
        service.Setup(s => s.AcceptInvitationByIdAsync(14, 7, "organizer@example.com"))
            .ReturnsAsync(new EventInvitationDecisionResponse
            {
                Invitation = new EventInvitationResponse
                {
                    Id = 14,
                    EventId = 9,
                    LifecycleStatus = "Accepted"
                }
            });

        var controller = CreateController(service.Object);

        var result = await controller.AcceptInvitationById(14);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<EventInvitationDecisionResponse>>().Subject;
        response.Data!.Invitation.LifecycleStatus.Should().Be("Accepted");
    }

    private static EventInvitationController CreateController(IEventInvitationService service)
    {
        var controller = new EventInvitationController(service);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "7"),
                    new Claim(ClaimTypes.Name, "organizer@example.com"),
                    new Claim(ClaimTypes.Role, "Organizer")
                ], "TestAuth"))
            }
        };

        return controller;
    }
}
