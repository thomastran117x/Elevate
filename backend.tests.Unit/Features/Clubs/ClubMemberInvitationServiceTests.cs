using System.Security.Cryptography;
using System.Text;

using backend.main.features.cache;
using backend.main.features.clubs;
using backend.main.features.clubs.follow;
using backend.main.features.clubs.follow.invitations;
using backend.main.features.profile;
using backend.main.features.profile.contracts;
using backend.main.infrastructure.database.core;
using backend.main.shared.exceptions.http;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

using StackExchange.Redis;

namespace backend.tests.Unit.Features.Clubs;

public class ClubMemberInvitationServiceTests
{
    private const int ClubId = 4;
    private const int OwnerUserId = 7;
    private const int RecipientUserId = 9;
    private const string ActorRole = "Organizer";

    private static string TokenKey(string tokenHash) => $"club:memberinvite:token:{tokenHash}";
    private static string ClubIndexKey(int clubId) => $"club:memberinvite:club:{clubId}";

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public AppDatabaseContext Db { get; }
        public FakeCacheService Cache { get; } = new();
        public Mock<IClubService> ClubServiceMock { get; } = new();
        public Mock<IFollowService> FollowServiceMock { get; } = new();
        public Mock<IUserRepository> UserRepositoryMock { get; } = new();
        public Mock<IPublisher> PublisherMock { get; } = new();
        public List<EmailMessage> Published { get; } = [];
        public ClubMemberInvitationService Service { get; }

        private Harness(SqliteConnection connection, AppDatabaseContext db)
        {
            _connection = connection;
            Db = db;

            PublisherMock
                .Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<EmailMessage>()))
                .Callback<string, EmailMessage>((_, m) => Published.Add(m))
                .Returns(Task.CompletedTask);

            Service = new ClubMemberInvitationService(
                Cache,
                ClubServiceMock.Object,
                FollowServiceMock.Object,
                UserRepositoryMock.Object,
                PublisherMock.Object,
                TimeProvider.System,
                Db);
        }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDatabaseContext>()
                .UseSqlite(connection)
                .Options;

            var db = new AppDatabaseContext(options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                new User { Id = OwnerUserId, Email = "owner@test.local", Usertype = "Organizer" },
                new User { Id = RecipientUserId, Email = "jordan@test.local", Usertype = "Participant" });
            await db.SaveChangesAsync();

            db.Clubs.Add(new Club
            {
                Id = ClubId,
                UserId = OwnerUserId,
                Name = "Chess Club",
                Description = "A club for tests.",
                Clubtype = ClubType.Other,
                ClubImage = "https://cdn.test/club.png"
            });
            await db.SaveChangesAsync();

            var harness = new Harness(connection, db);
            harness.SetupClub();
            return harness;
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

        public void SetupCanManage(bool canManage = true) =>
            ClubServiceMock
                .Setup(s => s.CanManageClubAsync(ClubId, OwnerUserId, ActorRole))
                .ReturnsAsync(canManage);

        public void SetupIsMember(bool isMember) =>
            FollowServiceMock
                .Setup(s => s.IsMemberAsync(ClubId, RecipientUserId))
                .ReturnsAsync(isMember);

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

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    // ---- Specific invites ----------------------------------------------------

    [Fact]
    public async Task CreateInvitationAsync_ByUsername_StoresInviteAndPublishesMemberEmail()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupIsMember(false);
        harness.SetupUserByUsername("jordan");

        var response = await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");

        response.RecipientUserId.Should().Be(RecipientUserId);
        response.RecipientEmail.Should().Be("jordan@test.local");

        harness.Published.Should().ContainSingle();
        harness.Published[0].Type.Should().Be(EmailMessageType.ClubMemberInvite);
        harness.Published[0].ClubName.Should().Be("Chess Club");
        harness.Published[0].Token.Should().NotBeNullOrWhiteSpace();

        var tokenJson = await harness.Cache.GetValueAsync(TokenKey(HashToken(harness.Published[0].Token!)));
        tokenJson.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateInvitationAsync_NotManager_ThrowsForbidden()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage(false);

        var act = () => harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");

        await act.Should().ThrowAsync<ForbiddenException>();
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateInvitationAsync_AlreadyMember_ThrowsConflict()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupIsMember(true);
        harness.SetupUserByUsername("jordan");

        var act = () => harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateInvitationAsync_Twice_DedupesAndSendsSingleEmail()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupIsMember(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");
        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");

        harness.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task AcceptAsync_Recipient_GrantsMembershipAndClearsKeys()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupIsMember(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");
        var token = harness.Published[0].Token!;

        var decision = await harness.Service.AcceptAsync(token, RecipientUserId);

        decision.Accepted.Should().BeTrue();
        decision.ClubId.Should().Be(ClubId);
        harness.ClubServiceMock.Verify(s => s.GrantMembershipFromInvitationAsync(ClubId, RecipientUserId), Times.Once);
        (await harness.Cache.GetValueAsync(TokenKey(HashToken(token)))).Should().BeNull();
    }

    [Fact]
    public async Task AcceptAsync_WrongUser_ThrowsForbiddenAndDoesNotGrant()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupIsMember(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");
        var token = harness.Published[0].Token!;

        var act = () => harness.Service.AcceptAsync(token, RecipientUserId + 99);

        await act.Should().ThrowAsync<ForbiddenException>();
        harness.ClubServiceMock.Verify(
            s => s.GrantMembershipFromInvitationAsync(It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Recipient_ReturnsAcceptAvailableForDirectInvite()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupIsMember(false);
        harness.SetupUserByUsername("jordan");

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");
        var token = harness.Published[0].Token!;

        var result = await harness.Service.ResolveAsync(token, RecipientUserId);

        result.State.Should().Be(ClubMemberInvitationResolveState.AcceptAvailable.ToString());
        result.Source.Should().Be(ClubMemberInvitationSource.DirectInvite.ToString());
        result.CanAccept.Should().BeTrue();
        result.CanDecline.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_AlreadyMember_ReturnsAlreadyMember()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.SetupUserByUsername("jordan");
        // Not a member at create time, but is a member when resolving.
        harness.FollowServiceMock
            .SetupSequence(s => s.IsMemberAsync(ClubId, RecipientUserId))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        await harness.Service.CreateInvitationAsync(ClubId, OwnerUserId, ActorRole, "jordan");
        var token = harness.Published[0].Token!;

        var result = await harness.Service.ResolveAsync(token, RecipientUserId);

        result.State.Should().Be(ClubMemberInvitationResolveState.AlreadyMember.ToString());
        result.CanAccept.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_UnknownToken_ReturnsInvalid()
    {
        await using var harness = await Harness.CreateAsync();

        var result = await harness.Service.ResolveAsync("does-not-exist", RecipientUserId);

        result.State.Should().Be(ClubMemberInvitationResolveState.Invalid.ToString());
    }

    // ---- Invite links --------------------------------------------------------

    [Fact]
    public async Task CreateLinkAsync_StoresLinkAndReturnsShareUrl()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();

        var response = await harness.Service.CreateLinkAsync(
            ClubId, OwnerUserId, ActorRole, DateTime.UtcNow.AddDays(3), maxRedemptions: 5);

        response.ShareUrl.Should().StartWith("/clubs/member-invite?token=");
        response.MaxRedemptions.Should().Be(5);
        (await harness.Db.ClubInvitationLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateLinkAsync_PastExpiry_ThrowsBadRequest()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();

        var act = () => harness.Service.CreateLinkAsync(
            ClubId, OwnerUserId, ActorRole, DateTime.UtcNow.AddDays(-1), maxRedemptions: null);

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task RedeemLinkAsync_GrantsMembershipAndIncrementsCount()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();
        harness.FollowServiceMock
            .Setup(s => s.IsMemberAsync(ClubId, RecipientUserId))
            .ReturnsAsync(false);

        var token = "share-token-123";
        harness.Db.ClubInvitationLinks.Add(new ClubInvitationLink
        {
            ClubId = ClubId,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddDays(2),
            MaxRedemptions = 3,
            RedemptionCount = 0,
            CreatedByUserId = OwnerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var decision = await harness.Service.RedeemLinkAsync(token, RecipientUserId);

        decision.Accepted.Should().BeTrue();
        decision.ClubId.Should().Be(ClubId);
        harness.ClubServiceMock.Verify(s => s.GrantMembershipFromInvitationAsync(ClubId, RecipientUserId), Times.Once);

        var link = await harness.Db.ClubInvitationLinks.SingleAsync();
        link.RedemptionCount.Should().Be(1);
    }

    [Fact]
    public async Task RedeemLinkAsync_Exhausted_ThrowsGone()
    {
        await using var harness = await Harness.CreateAsync();

        var token = "exhausted-token";
        harness.Db.ClubInvitationLinks.Add(new ClubInvitationLink
        {
            ClubId = ClubId,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddDays(2),
            MaxRedemptions = 1,
            RedemptionCount = 1,
            CreatedByUserId = OwnerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var act = () => harness.Service.RedeemLinkAsync(token, RecipientUserId);

        await act.Should().ThrowAsync<GoneException>();
    }

    [Fact]
    public async Task RedeemLinkAsync_Revoked_ThrowsGone()
    {
        await using var harness = await Harness.CreateAsync();

        var token = "revoked-token";
        harness.Db.ClubInvitationLinks.Add(new ClubInvitationLink
        {
            ClubId = ClubId,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddDays(2),
            MaxRedemptions = null,
            RedemptionCount = 0,
            CreatedByUserId = OwnerUserId,
            RevokedByUserId = OwnerUserId,
            RevokedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var act = () => harness.Service.RedeemLinkAsync(token, RecipientUserId);

        await act.Should().ThrowAsync<GoneException>();
    }

    [Fact]
    public async Task ResolveAsync_ValidLink_ReturnsAcceptAvailableForLinkSource()
    {
        await using var harness = await Harness.CreateAsync();
        harness.FollowServiceMock
            .Setup(s => s.IsMemberAsync(ClubId, RecipientUserId))
            .ReturnsAsync(false);

        var token = "resolve-link-token";
        harness.Db.ClubInvitationLinks.Add(new ClubInvitationLink
        {
            ClubId = ClubId,
            TokenHash = HashToken(token),
            ExpiresAt = DateTime.UtcNow.AddDays(2),
            MaxRedemptions = null,
            RedemptionCount = 0,
            CreatedByUserId = OwnerUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var result = await harness.Service.ResolveAsync(token, RecipientUserId);

        result.State.Should().Be(ClubMemberInvitationResolveState.AcceptAvailable.ToString());
        result.Source.Should().Be(ClubMemberInvitationSource.Link.ToString());
        result.CanAccept.Should().BeTrue();
        // Links cannot be declined.
        result.CanDecline.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeLinkAsync_MarksRevoked()
    {
        await using var harness = await Harness.CreateAsync();
        harness.SetupCanManage();

        var created = await harness.Service.CreateLinkAsync(
            ClubId, OwnerUserId, ActorRole, DateTime.UtcNow.AddDays(3), maxRedemptions: null);

        var response = await harness.Service.RevokeLinkAsync(ClubId, created.Id, OwnerUserId, ActorRole);

        response.IsRevoked.Should().BeTrue();
        (await harness.Db.ClubInvitationLinks.SingleAsync()).RevokedAtUtc.Should().NotBeNull();
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

        // Unused by ClubMemberInvitationService.
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
