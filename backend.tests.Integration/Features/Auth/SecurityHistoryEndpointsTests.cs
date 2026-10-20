using System.Net;
using System.Net.Http.Headers;

using backend.main.application.security;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.events;
using backend.main.features.auth.history;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The read side of the account security history: ownership, ordering, paging bounds, expiry,
/// and the deletion cascade. Rows are seeded directly; the producer and worker that write them
/// in production are covered by the pipeline tests.
/// </summary>
public sealed class SecurityHistoryEndpointsTests
{
    private const string Path = "/api/auth/security-events";

    [Fact]
    public async Task List_ShouldRequireAuthentication()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.GetAsync("/api/auth/security-events");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_ShouldReturnOnlyTheCallersUnexpiredEvents_NewestFirst()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await app.SeedUserAsync("history-owner@example.com");
        var other = await app.SeedUserAsync("history-other@example.com");
        await app.SeedKnownDeviceAsync(owner.Id, "history-owner-device");
        var now = DateTime.UtcNow;

        await SeedAsync(app,
            Entry(owner.Id, AccountSecurityEventKind.PasswordChanged, now.AddDays(-2)),
            Entry(owner.Id, AccountSecurityEventKind.MfaEnabled, now.AddDays(-1), factor: "totp"),
            Entry(owner.Id, AccountSecurityEventKind.ProviderLinked, now.AddDays(-91), expiresAt: now.AddDays(-1)),
            Entry(other.Id, AccountSecurityEventKind.SignedIn, now.AddMinutes(-5)));

        var session = await app.LoginApiAsync("history-owner", trustedDeviceToken: "history-owner-device");
        var response = await app.GetWithBearerAsync("/api/auth/security-events", session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var page = (await app.ReadApiResponseAsync<HistoryPage>(response)).Data!;
        page.TotalCount.Should().Be(2);
        page.Items.Select(i => i.Kind).Should().Equal("mfa_enabled", "password_changed");
        page.Items[0].Factor.Should().Be("totp");
        page.Items[0].Client.Should().Be("chrome");
        page.Items[0].DeviceClass.Should().Be("desktop");
        page.Items[0].Network.Should().Be("203.0.113.0/24");
        page.Items[0].Reference.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task List_ShouldPageWithAStableTiebreak()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await app.SeedUserAsync("history-pager@example.com");
        await app.SeedKnownDeviceAsync(owner.Id, "history-pager-device");
        var instant = DateTime.UtcNow.AddHours(-1);

        await SeedAsync(app, Enumerable.Range(0, 5)
            .Select(_ => Entry(owner.Id, AccountSecurityEventKind.SignedOut, instant))
            .ToArray());

        var session = await app.LoginApiAsync("history-pager", trustedDeviceToken: "history-pager-device");

        var first = await ReadPageAsync(app, session.AccessToken, "?page=1&pageSize=2");
        var second = await ReadPageAsync(app, session.AccessToken, "?page=2&pageSize=2");
        var third = await ReadPageAsync(app, session.AccessToken, "?page=3&pageSize=2");

        first.TotalCount.Should().Be(5);
        first.TotalPages.Should().Be(3);
        var ids = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList();
        ids.Should().HaveCount(5).And.OnlyHaveUniqueItems().And.BeInDescendingOrder();
    }

    [Theory]
    [InlineData("?pageSize=51")]
    [InlineData("?pageSize=0")]
    [InlineData("?page=0")]
    [InlineData("?page=501")]
    public async Task List_ShouldRejectOutOfRangePaging(string query)
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await app.SeedUserAsync("history-bounds@example.com");
        await app.SeedKnownDeviceAsync(owner.Id, "history-bounds-device");
        var session = await app.LoginApiAsync("history-bounds", trustedDeviceToken: "history-bounds-device");

        var response = await app.GetWithBearerAsync(Path + query, session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteAccount_ShouldRemoveTheHistory()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await app.SeedUserAsync("history-delete@example.com");
        await app.SeedKnownDeviceAsync(owner.Id, "history-delete-device");
        await SeedAsync(app, Entry(owner.Id, AccountSecurityEventKind.SignedIn, DateTime.UtcNow.AddMinutes(-1)));

        var session = await app.LoginApiAsync("history-delete", trustedDeviceToken: "history-delete-device");
        await app.CompleteSessionMfaByEmailAsync("history-delete@example.com", session.AccessToken);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var remaining = await app.QueryDbAsync(db =>
            db.AccountSecurityEvents.CountAsync(e => e.UserId == owner.Id));
        remaining.Should().Be(0);
    }

    private static async Task<HistoryPage> ReadPageAsync(AuthApiTestApp app, string accessToken, string query)
    {
        var response = await app.GetWithBearerAsync(Path + query, accessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        return (await app.ReadApiResponseAsync<HistoryPage>(response)).Data!;
    }

    private static Task SeedAsync(AuthApiTestApp app, params AccountSecurityEvent[] entries) =>
        app.QueryDbAsync(async db =>
        {
            db.AccountSecurityEvents.AddRange(entries);
            return await db.SaveChangesAsync();
        });

    private static AccountSecurityEvent Entry(
        int userId,
        AccountSecurityEventKind kind,
        DateTime occurredAt,
        string? factor = null,
        DateTime? expiresAt = null) => new()
        {
            MessageId = Guid.NewGuid(),
            UserId = userId,
            Kind = kind,
            EventName = AuthSecurityEvents.FactorChanged.Name,
            Factor = factor,
            Client = SecurityHistoryClient.Chrome,
            DeviceClass = AuthDeviceClass.Desktop,
            Network = "203.0.113.0/24",
            CorrelationId = Guid.NewGuid().ToString("D"),
            OccurredAtUtc = occurredAt,
            RecordedAtUtc = occurredAt,
            ExpiresAtUtc = expiresAt ?? occurredAt.AddDays(90),
        };

    private sealed record HistoryPage(
        List<AccountSecurityEventResponse> Items,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages);
}
