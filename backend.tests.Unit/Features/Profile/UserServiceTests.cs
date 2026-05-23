using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.token;
using backend.main.features.clubs.follow;
using backend.main.features.profile;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

using backend.tests.Unit.Support;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Moq;

namespace backend.tests.Unit.Features.Profile;

public class UserServiceTests
{
    [Fact]
    public async Task GetUserByIdAsync_ShouldThrowWhenUserDoesNotExist()
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(repo => repo.GetUserAsync(44)).ReturnsAsync((User?)null);

        var service = CreateService(userRepository: repository);

        var act = () => service.GetUserByIdAsync(44);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("User with the id 44 is not found");
    }

    [Fact]
    public async Task UpdateUserStatusAsync_ShouldRevokeRefreshSessions()
    {
        var authRepository = new Mock<IAuthUserRepository>();
        authRepository.Setup(repo => repo.UpdateUserStatusAsync(9, true, "policy"))
            .ReturnsAsync(new UserStatusRecord
            {
                Id = 9,
                IsDisabled = true,
                DisabledReason = "policy",
                DisabledAtUtc = DateTime.UtcNow,
                AuthVersion = 4
            });

        var tokenService = new Mock<ITokenService>();
        var service = CreateService(authRepository: authRepository, tokenService: tokenService);

        var result = await service.UpdateUserStatusAsync(9, true, "policy");

        result.IsDisabled.Should().BeTrue();
        tokenService.Verify(service => service.RevokeAllRefreshSessionsAsync(9), Times.Once);
    }

    [Fact]
    public async Task UpdateAvatarAsync_ShouldUploadAndPersistAvatar()
    {
        var fileService = new Mock<IFileUploadService>();
        fileService.Setup(service => service.UploadImageAsync(It.IsAny<IFormFile>(), "users"))
            .ReturnsAsync("https://cdn.test/users/avatar.png");

        var repository = new Mock<IUserRepository>();
        repository.Setup(repo => repo.GetUserAsync(7))
            .ReturnsAsync(new TestUserBuilder().WithId(7).WithEmail("user@example.com").Build());
        repository.Setup(repo => repo.UpdatePartialAsync(It.Is<User>(user => user.Avatar == "https://cdn.test/users/avatar.png")))
            .ReturnsAsync((User user) => user);

        var service = CreateService(userRepository: repository, fileService: fileService);
        var formFile = new FormFile(new MemoryStream("avatar"u8.ToArray()), 0, 6, "avatar", "avatar.png");

        var updated = await service.UpdateAvatarAsync(7, formFile);

        updated!.Avatar.Should().Be("https://cdn.test/users/avatar.png");
    }

    [Fact]
    public async Task GetUserFollowingsAsync_ShouldReturnFollowingsFromFollowService()
    {
        var followService = new Mock<IFollowService>();
        followService.Setup(service => service.GetFollowsByUserAsync(7, 2, 5))
            .ReturnsAsync([
                new FollowClub
                {
                    Id = 4,
                    UserId = 7,
                    ClubId = 9
                }
            ]);

        var service = CreateService(followService: followService);

        var result = (await service.GetUserFollowingsAsync(7, 2, 5)).ToList();

        result.Should().ContainSingle();
        result[0].ClubId.Should().Be(9);
    }

    private static UserService CreateService(
        Mock<IUserRepository>? userRepository = null,
        Mock<IAuthUserRepository>? authRepository = null,
        Mock<IFileUploadService>? fileService = null,
        Mock<IFollowService>? followService = null,
        Mock<ITokenService>? tokenService = null)
    {
        userRepository ??= new Mock<IUserRepository>();
        authRepository ??= new Mock<IAuthUserRepository>();
        fileService ??= new Mock<IFileUploadService>();
        followService ??= new Mock<IFollowService>();
        tokenService ??= new Mock<ITokenService>();

        return new UserService(
            userRepository.Object,
            authRepository.Object,
            fileService.Object,
            followService.Object,
            tokenService.Object);
    }
}
