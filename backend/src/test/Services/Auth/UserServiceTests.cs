using backend.main.configurations.security;
using backend.main.models.core;
using backend.main.repositories.interfaces;
using backend.main.services.implementation;
using backend.main.services.interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace backend.test;

public class UserServiceTests
{
    [Fact]
    public async Task UpdateUserStatusAsync_RevokesRefreshSessions()
    {
        var userRepository = new Mock<IUserRepository>();
        var fileService = new Mock<IFileUploadService>(MockBehavior.Strict);
        var followService = new Mock<IFollowService>(MockBehavior.Strict);
        var tokenService = new Mock<ITokenService>();

        userRepository.Setup(repository => repository.UpdateUserStatusAsync(42, true, "Abuse"))
            .ReturnsAsync(new User
            {
                Id = 42,
                Email = "user@example.com",
                Usertype = AuthRoles.Participant,
                IsDisabled = true,
                DisabledReason = "Abuse",
                AuthVersion = 2,
            });
        tokenService.Setup(service => service.RevokeAllRefreshSessionsAsync(42))
            .Returns(Task.CompletedTask);

        var service = new UserService(
            userRepository.Object,
            fileService.Object,
            followService.Object,
            tokenService.Object
        );

        var user = await service.UpdateUserStatusAsync(42, true, "Abuse");

        user.IsDisabled.Should().BeTrue();
        tokenService.Verify(service => service.RevokeAllRefreshSessionsAsync(42), Times.Once);
    }
}
