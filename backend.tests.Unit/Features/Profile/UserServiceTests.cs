using backend.main.features.auth;
using backend.main.features.auth.contracts;
using backend.main.features.auth.token;
using backend.main.features.cache;
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
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<User>(
                "user:44",
                It.IsAny<Func<Task<User?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync((User?)null);

        var service = CreateService(refreshCache: refreshCache);

        var act = () => service.GetUserByIdAsync(44);

        await act.Should().ThrowAsync<ResourceNotFoundException>()
            .WithMessage("User with the id 44 is not found");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnCachedUser()
    {
        var cachedUser = new TestUserBuilder().WithId(5).WithEmail("cached@example.com").Build();
        var refreshCache = new Mock<IRefreshAheadCache>();
        refreshCache
            .Setup(c => c.GetOrSetAsync<User>(
                "user:5",
                It.IsAny<Func<Task<User?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<double>(),
                It.IsAny<System.Text.Json.JsonSerializerOptions?>()))
            .ReturnsAsync(cachedUser);

        var repository = new Mock<IUserRepository>();
        var service = CreateService(userRepository: repository, refreshCache: refreshCache);

        var result = await service.GetUserByIdAsync(5);

        result.Should().Be(cachedUser);
        repository.Verify(r => r.GetUserAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUserStatusAsync_ShouldRevokeRefreshSessionsAndInvalidateCache()
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
        var refreshCache = new Mock<IRefreshAheadCache>();
        var service = CreateService(authRepository: authRepository, tokenService: tokenService, refreshCache: refreshCache);

        var result = await service.UpdateUserStatusAsync(9, true, "policy");

        result.IsDisabled.Should().BeTrue();
        tokenService.Verify(service => service.RevokeAllRefreshSessionsAsync(9), Times.Once);
        refreshCache.Verify(c => c.RemoveAsync("user:9"), Times.Once);
    }

    [Fact]
    public async Task UpdateAvatarAsync_ShouldUploadAndPersistAvatarAndInvalidateCache()
    {
        var blobService = new Mock<IAzureBlobService>();
        blobService.Setup(service => service.UploadImageAsync(It.IsAny<IFormFile>(), "users"))
            .ReturnsAsync("https://cdn.test/users/avatar.png");

        var repository = new Mock<IUserRepository>();
        repository.Setup(repo => repo.GetUserAsync(7))
            .ReturnsAsync(new TestUserBuilder().WithId(7).WithEmail("user@example.com").Build());
        repository.Setup(repo => repo.UpdatePartialAsync(It.Is<User>(user => user.Avatar == "https://cdn.test/users/avatar.png")))
            .ReturnsAsync((User user) => user);

        var refreshCache = new Mock<IRefreshAheadCache>();
        var service = CreateService(userRepository: repository, blobService: blobService, refreshCache: refreshCache);
        var formFile = new FormFile(new MemoryStream("avatar"u8.ToArray()), 0, 6, "avatar", "avatar.png");

        var updated = await service.UpdateAvatarAsync(7, formFile);

        updated!.Avatar.Should().Be("https://cdn.test/users/avatar.png");
        refreshCache.Verify(c => c.RemoveAsync("user:7"), Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldInvalidateCacheAfterUpdate()
    {
        var user = new TestUserBuilder().WithId(3).WithEmail("user@example.com").Build();
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.UpdatePartialAsync(user)).ReturnsAsync(user);

        var refreshCache = new Mock<IRefreshAheadCache>();
        var service = CreateService(userRepository: repository, refreshCache: refreshCache);

        await service.UpdateUserAsync(3, user);

        refreshCache.Verify(c => c.RemoveAsync("user:3"), Times.Once);
    }

    [Fact]
    public async Task DeleteUserAsync_ShouldDeleteOrphanedBlobsAndInvalidateCache()
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(r => r.DeleteUserAsync(5))
            .ReturnsAsync(new[] { "https://cdn.test/users/a.png", "https://cdn.test/clubs/b.png" });

        var blobService = new Mock<IAzureBlobService>();
        var refreshCache = new Mock<IRefreshAheadCache>();
        var service = CreateService(userRepository: repository, blobService: blobService, refreshCache: refreshCache);

        await service.DeleteUserAsync(5);

        blobService.Verify(b => b.DeleteBlobAsync("https://cdn.test/users/a.png"), Times.Once);
        blobService.Verify(b => b.DeleteBlobAsync("https://cdn.test/clubs/b.png"), Times.Once);
        refreshCache.Verify(c => c.RemoveAsync("user:5"), Times.Once);
    }

    [Fact]
    public async Task UpdateAvatarAsync_WhenPersistFails_ShouldDeleteUploadedBlobAndRethrow()
    {
        var blobService = new Mock<IAzureBlobService>();
        blobService.Setup(service => service.UploadImageAsync(It.IsAny<IFormFile>(), "users"))
            .ReturnsAsync("https://cdn.test/users/new.png");

        var repository = new Mock<IUserRepository>();
        repository.Setup(repo => repo.GetUserAsync(7))
            .ReturnsAsync(new TestUserBuilder().WithId(7).WithEmail("user@example.com").Build());
        repository.Setup(repo => repo.UpdatePartialAsync(It.IsAny<User>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var service = CreateService(userRepository: repository, blobService: blobService);
        var formFile = new FormFile(new MemoryStream("avatar"u8.ToArray()), 0, 6, "avatar", "avatar.png");

        var act = () => service.UpdateAvatarAsync(7, formFile);

        // The persist failure surfaces, but the just-uploaded blob is cleaned up first.
        await act.Should().ThrowAsync<InvalidOperationException>();
        blobService.Verify(b => b.DeleteBlobAsync("https://cdn.test/users/new.png"), Times.Once);
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
        Mock<IAzureBlobService>? blobService = null,
        Mock<IFollowService>? followService = null,
        Mock<ITokenService>? tokenService = null,
        Mock<IRefreshAheadCache>? refreshCache = null)
    {
        userRepository ??= new Mock<IUserRepository>();
        authRepository ??= new Mock<IAuthUserRepository>();
        blobService ??= new Mock<IAzureBlobService>();
        followService ??= new Mock<IFollowService>();
        tokenService ??= new Mock<ITokenService>();
        refreshCache ??= new Mock<IRefreshAheadCache>();

        return new UserService(
            userRepository.Object,
            authRepository.Object,
            blobService.Object,
            followService.Object,
            tokenService.Object,
            refreshCache.Object);
    }
}
