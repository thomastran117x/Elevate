using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.invitations;
using backend.main.features.clubs.staff;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using FluentAssertions;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Clubs;

public class ClubInvitationServiceTests
{
    private const int ClubId = 7;
    private const int OwnerUserId = 1;
    private const int RecipientUserId = 5;
    private const string OwnerRole = "Organizer";

    private static string TokenKey(string tokenHash) => $"club:invite:token:{tokenHash}";
    private static string ClubIndexKey(int clubId) => $"club:invite:club:{clubId}";

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class Harness
    {
        public FakeCacheService Cache { get; } = new();
        public Mock<IClubService> ClubServiceMock { get; } = new(MockBehavior.Strict);
        public Mock<IUserRepository> UserRepositoryMock { get; } = new();
        public Mock<IPublisher> PublisherMock { get; } = new();
        public List<EmailMessage> Published { get; } = [];
        public ClubInvitationService Service { get; }

        public Harness()
        {
            PublisherMock
                .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
                .Callback<string, EmailMessage>((_, m) => Published.Add(m))
                .Returns(Task.CompletedTask);

            Service = new ClubInvitationService(
                Cache,
                ClubServiceMock.Object,
                UserRepositoryMock.Object,
                PublisherMock.Object,
                TimeProvider.System);
        }

        public void SetupClub() =>
            ClubServiceMock
                .Setup(s => s.GetClub(ClubId))
                .ReturnsAsync(new Club
                {
                    Id = ClubId,
                    Name = "Chess Club",
                    Description = "",
                    Clubtype = ClubType.Other,
                    ClubImage = "https://cdn.test/club.png",
                    UserId = OwnerUserId
                });

        public void SetupOwner(bool isOwner = true) =>
            ClubServiceMock
                .Setup(s => s.IsClubOwnerAsync(ClubId, OwnerUserId, OwnerRole))
                .ReturnsAsync(isOwner);

        public void SetupExistingStaff(bool isStaff) =>
            ClubServiceMock
                .Setup(s => s.IsClubStaffMemberAsync(ClubId, RecipientUserId))
                .ReturnsAsync(isStaff);

        public void SetupUserByUsername(string username) =>
            UserRepositoryMock
                .Setup(r => r.GetProfileByUsernameAsync(username))
                .ReturnsAsync(new UserProfileRecord
                {
                    Id = RecipientUserId,
                    Email = "jordan@test.local",
                    Username = username,
                    Usertype = "Participant"
                });
    }

    [Fact]
    public async Task CreateInvitationAsync_ByUsername_StoresInviteAndPublishesEmail()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        var response = await harness.Service.CreateInvitationAsync(
            ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);

        response.RecipientUserId.Should().Be(RecipientUserId);
        response.RecipientEmail.Should().Be("jordan@test.local");
        response.Role.Should().Be("Manager");

        harness.Published.Should().ContainSingle();
        harness.Published[0].Type.Should().Be(EmailMessageType.ClubStaffInvite);
        harness.Published[0].ClubName.Should().Be("Chess Club");
        harness.Published[0].Token.Should().NotBeNullOrWhiteSpace();

        // Token key + index field both persisted.
        var indexJson = await harness.Cache.HashGetAsync(ClubIndexKey(ClubId), RecipientUserId.ToString());
        indexJson.Should().NotBeNull();
        var tokenJson = await harness.Cache.GetValueAsync(TokenKey(HashToken(harness.Published[0].Token!)));
        tokenJson.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateInvitationAsync_ByEmail_ResolvesViaEmailLookup()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.UserRepositoryMock
            .Setup(r => r.GetProfileByEmailAsync("jordan@test.local"))
            .ReturnsAsync(new UserProfileRecord
            {
                Id = RecipientUserId,
                Email = "jordan@test.local",
                Username = "jordan",
                Usertype = "Participant"
            });

        var response = await harness.Service.CreateInvitationAsync(
            ClubId, OwnerUserId, OwnerRole, "Jordan@Test.Local", ClubStaffRole.Volunteer);

        response.RecipientUserId.Should().Be(RecipientUserId);
        response.Role.Should().Be("Volunteer");
        harness.UserRepositoryMock.Verify(r => r.GetProfileByEmailAsync("jordan@test.local"), Times.Once);
    }

    [Fact]
    public async Task CreateInvitationAsync_UnknownIdentifier_ThrowsNotFound()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.UserRepositoryMock
            .Setup(r => r.GetProfileByUsernameAsync("ghost"))
            .ReturnsAsync((UserProfileRecord?)null);

        var act = () => harness.Service.CreateInvitationAsync(
            ClubId, OwnerUserId, OwnerRole, "ghost", ClubStaffRole.Manager);

        await act.Should().ThrowAsync<ResourceNotFoundException>();
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateInvitationAsync_NotOwner_ThrowsForbidden()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner(isOwner: false);

        var act = () => harness.Service.CreateInvitationAsync(
            ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateInvitationAsync_AlreadyStaff_ThrowsConflict()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(true);
        harness.SetupUserByUsername("jordan");

        var act = () => harness.Service.CreateInvitationAsync(
            ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateInvitationAsync_Twice_DedupesAndSendsSingleEmail()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);
        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);

        harness.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptInvitationAsync_Recipient_GrantsStaffAndClearsKeys()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");
        harness.ClubServiceMock
            .Setup(s => s.GrantStaffFromInvitationAsync(ClubId, RecipientUserId, ClubStaffRole.Manager, OwnerUserId))
            .ReturnsAsync(new ClubStaff { ClubId = ClubId, UserId = RecipientUserId, Role = ClubStaffRole.Manager });

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);
        var token = harness.Published[0].Token!;

        var decision = await harness.Service.AcceptInvitationAsync(token, RecipientUserId, "jordan@test.local");

        decision.Accepted.Should().BeTrue();
        decision.ClubId.Should().Be(ClubId);
        harness.ClubServiceMock.Verify(
            s => s.GrantStaffFromInvitationAsync(ClubId, RecipientUserId, ClubStaffRole.Manager, OwnerUserId),
            Times.Once);

        (await harness.Cache.GetValueAsync(TokenKey(HashToken(token)))).Should().BeNull();
        (await harness.Cache.HashGetAsync(ClubIndexKey(ClubId), RecipientUserId.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task AcceptInvitationAsync_WrongUser_ThrowsForbiddenAndDoesNotGrant()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);
        var token = harness.Published[0].Token!;

        var act = () => harness.Service.AcceptInvitationAsync(token, RecipientUserId + 99, "intruder@test.local");

        await act.Should().ThrowAsync<ForbiddenException>();
        harness.ClubServiceMock.Verify(
            s => s.GrantStaffFromInvitationAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ClubStaffRole>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task AcceptInvitationAsync_ExpiredToken_ThrowsGone()
    {
        var harness = new Harness();
        var token = "expired-token";
        var invite = new PendingClubInvite
        {
            ClubId = ClubId,
            RecipientUserId = RecipientUserId,
            RecipientEmail = "jordan@test.local",
            Role = ClubStaffRole.Manager,
            CreatedByUserId = OwnerUserId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-20),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
            TokenHash = HashToken(token)
        };
        await harness.Cache.SetValueAsync(TokenKey(HashToken(token)), JsonSerializer.Serialize(invite));

        var act = () => harness.Service.AcceptInvitationAsync(token, RecipientUserId, "jordan@test.local");

        await act.Should().ThrowAsync<GoneException>();
    }

    [Fact]
    public async Task ResolveInvitationAsync_MissingToken_ReturnsInvalid()
    {
        var harness = new Harness();

        var result = await harness.Service.ResolveInvitationAsync("does-not-exist", RecipientUserId);

        result.State.Should().Be(ClubInvitationResolveState.Invalid.ToString());
        result.CanAccept.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveInvitationAsync_Unauthenticated_ReturnsLoginRequired()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);
        var token = harness.Published[0].Token!;

        var result = await harness.Service.ResolveInvitationAsync(token, userId: null);

        result.State.Should().Be(ClubInvitationResolveState.LoginRequired.ToString());
        result.RequiresAuthentication.Should().BeTrue();
        result.Club!.Name.Should().Be("Chess Club");
    }

    [Fact]
    public async Task ResolveInvitationAsync_Recipient_ReturnsAcceptAvailable()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Volunteer);
        var token = harness.Published[0].Token!;

        var result = await harness.Service.ResolveInvitationAsync(token, RecipientUserId);

        result.State.Should().Be(ClubInvitationResolveState.AcceptAvailable.ToString());
        result.CanAccept.Should().BeTrue();
        result.Role.Should().Be("Volunteer");
    }

    [Fact]
    public async Task DeclineInvitationAsync_Recipient_ClearsKeys()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);
        var token = harness.Published[0].Token!;

        var decision = await harness.Service.DeclineInvitationAsync(token, RecipientUserId);

        decision.Accepted.Should().BeFalse();
        (await harness.Cache.GetValueAsync(TokenKey(HashToken(token)))).Should().BeNull();
        (await harness.Cache.HashGetAsync(ClubIndexKey(ClubId), RecipientUserId.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task RevokeInvitationAsync_Owner_RemovesPendingInvite()
    {
        var harness = new Harness();
        harness.SetupClub();
        harness.SetupOwner();
        harness.SetupExistingStaff(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, OwnerRole, "jordan", ClubStaffRole.Manager);
        var token = harness.Published[0].Token!;

        await harness.Service.RevokeInvitationAsync(ClubId, RecipientUserId, OwnerUserId, OwnerRole);

        (await harness.Cache.GetValueAsync(TokenKey(HashToken(token)))).Should().BeNull();
        var invitations = await harness.Service.GetClubInvitationsAsync(ClubId, OwnerUserId, OwnerRole);
        invitations.Should().BeEmpty();
    }

    /// <summary>Minimal in-memory ICacheService covering the operations the service exercises.</summary>
    private sealed class FakeCacheService : ICacheService
    {
        private readonly Dictionary<string, string> _strings = [];
        private readonly Dictionary<string, Dictionary<string, string>> _hashes = [];

        public Task<bool> SetValueAsync(string key, string value, TimeSpan? expiry = null)
        {
            _strings[key] = value;
            return Task.FromResult(true);
        }

        public Task<string?> GetValueAsync(string key) =>
            Task.FromResult(_strings.TryGetValue(key, out var v) ? v : null);

        public Task<bool> HashSetAsync(string key, string field, string value)
        {
            if (!_hashes.TryGetValue(key, out var hash))
            {
                hash = [];
                _hashes[key] = hash;
            }

            hash[field] = value;
            return Task.FromResult(true);
        }

        public Task<string?> HashGetAsync(string key, string field) =>
            Task.FromResult(_hashes.TryGetValue(key, out var hash) && hash.TryGetValue(field, out var v) ? v : null);

        public Task<Dictionary<string, string>> HashGetAllAsync(string key) =>
            Task.FromResult(_hashes.TryGetValue(key, out var hash)
                ? new Dictionary<string, string>(hash)
                : []);

        public Task<bool> HashDeleteAsync(string key, string field) =>
            Task.FromResult(_hashes.TryGetValue(key, out var hash) && hash.Remove(field));

        public Task<bool> DeleteKeyAsync(string key) =>
            Task.FromResult(_strings.Remove(key) | _hashes.Remove(key));

        public Task<bool> SetExpiryAsync(string key, TimeSpan expiry) => Task.FromResult(true);

        // Unused by ClubInvitationService.
        public Task<long> IncrementAsync(string key, long value = 1) => throw new System.NotImplementedException();
        public Task<long> DecrementAsync(string key, long value = 1) => throw new System.NotImplementedException();
        public Task<bool> SetAddAsync(string key, string value) => throw new System.NotImplementedException();
        public Task<bool> SetRemoveAsync(string key, string value) => throw new System.NotImplementedException();
        public Task<string[]> SetMembersAsync(string key) => throw new System.NotImplementedException();
        public Task<long> ListLeftPushAsync(string key, string value) => throw new System.NotImplementedException();
        public Task<long> ListRightPushAsync(string key, string value) => throw new System.NotImplementedException();
        public Task<string?> ListLeftPopAsync(string key) => throw new System.NotImplementedException();
        public Task<string?> ListRightPopAsync(string key) => throw new System.NotImplementedException();
        public Task<bool> KeyExistsAsync(string key) => throw new System.NotImplementedException();
        public Task<TimeSpan?> GetTTLAsync(string key) => throw new System.NotImplementedException();
        public IEnumerable<string> ScanKeys(IServer server, string pattern) => throw new System.NotImplementedException();
        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) => throw new System.NotImplementedException();
        public Task<bool> ReleaseLockAsync(string key, string value) => throw new System.NotImplementedException();
        public IServer GetServer() => throw new System.NotImplementedException();
        public Task<Dictionary<string, string?>> GetManyAsync(IEnumerable<string> keys) => throw new System.NotImplementedException();
        public Task<object> EvalAsync(string script, RedisKey[] keys, RedisValue[] values) => throw new System.NotImplementedException();
    }
}
