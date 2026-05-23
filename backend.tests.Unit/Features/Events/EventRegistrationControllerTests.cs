using System.Security.Claims;

using backend.main.features.events;
using backend.main.features.events.registration;
using backend.main.features.events.registration.contracts.requests;
using backend.main.features.events.registration.contracts.responses;
using backend.main.shared.responses;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace backend.tests.Unit.Features.Events;

public class EventRegistrationControllerTests
{
    [Fact]
    public async Task Register_ShouldReturnCreatedMessage()
    {
        var registrationService = new Mock<IEventRegistrationService>();
        var controller = CreateController(registrationService.Object, Mock.Of<IEventsService>());

        var result = await controller.Register(9);

        var created = result.Should().BeOfType<ObjectResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.Value.Should().BeOfType<MessageResponse>()
            .Which.Message.Should().Contain("Successfully registered for event with ID 9.");
        registrationService.Verify(service => service.RegisterAsync(9, 7, "Organizer"), Times.Once);
    }

    [Fact]
    public async Task CheckRegistration_ShouldReturnCurrentMembershipState()
    {
        var registrationService = new Mock<IEventRegistrationService>();
        registrationService.Setup(service => service.IsRegisteredAsync(9, 7, "Organizer"))
            .ReturnsAsync(true);

        var controller = CreateController(registrationService.Object, Mock.Of<IEventsService>());

        var result = await controller.CheckRegistration(9);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        response.Data.Should().NotBeNull();
        response.Data!.ToString().Should().Contain("True");
    }

    [Fact]
    public async Task BatchRegister_ShouldReturnMultiStatusSummary()
    {
        var registrationService = new Mock<IEventRegistrationService>();
        registrationService.Setup(service => service.BatchRegisterAsync(7, "Organizer", It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new BatchRegistrationResultResponse
            {
                Succeeded = [9],
                Failed =
                [
                    new BatchRegistrationFailure
                    {
                        EventId = 10,
                        Reason = "Already registered"
                    }
                ]
            });

        var controller = CreateController(registrationService.Object, Mock.Of<IEventsService>());

        var result = await controller.BatchRegister(new BatchRegistrationRequest
        {
            EventIds = [9, 10]
        });

        var response = result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(207);
        response.Value.Should().BeOfType<ApiResponse<BatchRegistrationResultResponse>>()
            .Which.Data!.Succeeded.Should().ContainSingle().Which.Should().Be(9);
    }

    private static EventRegistrationController CreateController(
        IEventRegistrationService registrationService,
        IEventsService eventsService)
    {
        var controller = new EventRegistrationController(registrationService, eventsService);
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
