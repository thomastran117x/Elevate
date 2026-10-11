using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.token;
using backend.main.features.clubs.staff;
using backend.main.features.events;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace backend.tests.Integration.Application.Security;

/// <summary>
/// End-to-end checks for authenticated-by-default authorization: the public surface stays
/// reachable anonymously, everything else answers the shared 401 envelope, global-role policies
/// answer the 403 envelope, and resource ownership and club staff roles stay decided by the
/// services.
/// </summary>
public class AuthorizationMatrixEndpointsTests
{
    private const string PolicyForbiddenMessage = "You do not have permission to access this resource";

    [Fact]
    public async Task EveryRoutedEndpoint_ShouldDeclareAnAuthorizationClassification()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var unclassified = app.Endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
                && endpoint.Metadata.GetMetadata<IAuthorizeData>() is null)
            .Select(endpoint => endpoint.DisplayName ?? endpoint.RoutePattern.RawText)
            .ToList();

        unclassified.Should().BeEmpty("controllers, hubs, and minimal endpoints must each opt in or out explicitly");
    }

    public static TheoryData<string, string> PublicRoutesWithoutData => new()
    {
        { "GET", "/api/auth/csrf" },
        { "GET", "/api/events?page=1&pageSize=20" },
        { "POST", "/api/events/search" },
        { "GET", "/api/clubs" },
        { "POST", "/api/clubs/search" },
        { "POST", "/api/events/invitations/resolve" },
        { "POST", "/api/clubs/invitations/resolve" },
        { "POST", "/api/clubs/members/invitations/resolve" },
        { "POST", "/api/hubs/clubs/negotiate?negotiateVersion=1" },
        { "GET", "/openapi.json" },
    };

    [Theory]
    [MemberData(nameof(PublicRoutesWithoutData))]
    public async Task PublicEndpoints_ShouldNotChallengeAnonymousCallers(string method, string path)
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST" && !path.Contains("/negotiate", StringComparison.Ordinal))
            request.Content = JsonContent.Create(new { token = "not-a-real-invitation-token" });

        var response = await app.Client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized, await app.DescribeFailureAsync(response));
    }

    [Fact]
    public async Task PublicResourceReads_ShouldStayReachableAnonymously()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "authz-public-owner@example.com", "Organizer");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Authz Public Club");
        var eventId = await CreatePublishedEventAsync(app, owner.AccessToken, clubId, "Authz Public Event");

        var reads = new[]
        {
            await app.Client.GetAsync($"/api/clubs/{clubId}"),
            await app.Client.GetAsync($"/api/clubs/{clubId}/members"),
            await app.Client.GetAsync($"/api/clubs/{clubId}/reviews"),
            await app.Client.GetAsync($"/api/events/{eventId}"),
            await app.Client.GetAsync($"/api/events/clubs/{clubId}"),
            await app.Client.GetAsync($"/api/events/{eventId}/registrations"),
            await app.Client.GetAsync($"/api/events/batch?ids={eventId}"),
            await app.Client.GetAsync("/api/profile/authz-public-owner"),
        };

        foreach (var response in reads)
            response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
    }

    public static TheoryData<string, string> ProtectedRoutes => new()
    {
        { "GET", "/api/auth/me" },
        { "GET", "/api/profile" },
        { "GET", "/api/clubs/managed" },
        { "GET", "/api/events/me/favourites/ids" },
        { "GET", "/api/payments/me" },
        { "GET", "/api/users/1/reviews" },
        { "PATCH", "/api/admin/users/1/status" },
        { "POST", "/api/admin/events/reindex" },
    };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task ProtectedEndpoints_ShouldReturnTheUnauthorizedEnvelope_WithoutAToken(string method, string path)
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertEnvelopeAsync(response, "UNAUTHORIZED", "Missing access token");
    }

    [Fact]
    public async Task ProtectedEndpoints_ShouldReturnTheUnauthorizedEnvelope_ForAnInvalidToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var response = await app.Client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, "/api/auth/me", "not-a-jwt"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertEnvelopeAsync(response, "UNAUTHORIZED", "Invalid or expired access token");
    }

    [Fact]
    public async Task UnmatchedRoutes_ShouldStillReturnNotFound_ForAnonymousAndAuthenticatedCallers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await CreateSessionAsync(app, "authz-unmatched@example.com", "Participant");

        var anonymous = await app.Client.GetAsync("/api/authorization-matrix/does-not-exist");
        var authenticated = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            "/api/authorization-matrix/does-not-exist",
            session.AccessToken));

        anonymous.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertEnvelopeAsync(anonymous, "RESOURCE_NOT_FOUND", "Resource not found.");
        authenticated.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertEnvelopeAsync(authenticated, "RESOURCE_NOT_FOUND", "Resource not found.");
    }

    [Theory]
    [InlineData("Participant")]
    [InlineData("Organizer")]
    [InlineData("Volunteer")]
    public async Task AdminOnlyEndpoints_ShouldForbidNonAdminGlobalRoles(string role)
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var session = await CreateSessionAsync(app, $"authz-{role.ToLowerInvariant()}@example.com", role);

        var responses = new[]
        {
            await app.Client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, "/api/users/1/reviews", session.AccessToken)),
            await app.Client.SendAsync(CreateAuthorizedRequest(
                HttpMethod.Patch,
                "/api/admin/users/1/status",
                session.AccessToken,
                JsonContent.Create(new { isDisabled = true, reason = "policy" }))),
            await app.Client.SendAsync(CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/events/reindex", session.AccessToken)),
            await app.Client.SendAsync(CreateAuthorizedRequest(HttpMethod.Post, "/api/admin/clubs/reindex", session.AccessToken)),
        };

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await AssertEnvelopeAsync(response, "FORBIDDEN", PolicyForbiddenMessage);
        }
    }

    [Fact]
    public async Task AdminOnlyEndpoints_ShouldAdmitAdmins()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var admin = await CreateSessionAsync(app, "authz-admin@example.com", "Admin");
        var target = await app.SeedUserAsync("authz-admin-target@example.com");

        var reviews = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/users/{target.Id}/reviews",
            admin.AccessToken));

        reviews.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(reviews));
    }

    [Fact]
    public async Task ClubAndEventResourceRoles_ShouldStayAuthoritativeInTheServices()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "authz-res-owner@example.com", "Organizer");
        var manager = await CreateSessionAsync(app, "authz-res-manager@example.com", "Participant");
        var volunteer = await CreateSessionAsync(app, "authz-res-volunteer@example.com", "Volunteer");
        var outsider = await CreateSessionAsync(app, "authz-res-outsider@example.com", "Organizer");
        var admin = await CreateSessionAsync(app, "authz-res-admin@example.com", "Admin");

        var clubId = await CreateClubAsync(app, owner.AccessToken, "Authz Resource Club");
        var ownerUser = (await app.FindUserByEmailAsync("authz-res-owner@example.com"))!;
        var managerUser = (await app.FindUserByEmailAsync("authz-res-manager@example.com"))!;
        var volunteerUser = (await app.FindUserByEmailAsync("authz-res-volunteer@example.com"))!;
        await app.AddClubStaffAsync(clubId, managerUser.Id, ownerUser.Id, ClubStaffRole.Manager);
        await app.AddClubStaffAsync(clubId, volunteerUser.Id, ownerUser.Id, ClubStaffRole.Volunteer);
        var eventId = await CreatePublishedEventAsync(app, owner.AccessToken, clubId, "Authz Resource Event");

        var callers = new Dictionary<string, string>
        {
            ["owner"] = owner.AccessToken,
            ["manager"] = manager.AccessToken,
            ["volunteer"] = volunteer.AccessToken,
            ["outsider"] = outsider.AccessToken,
            ["admin"] = admin.AccessToken,
        };

        // Owner only, with no admin bypass.
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/clubs/{clubId}/staff",
            allowed: ["owner"]);

        // Club management: owner, managers, and admins.
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/events/clubs/{clubId}/manage",
            allowed: ["owner", "manager", "admin"]);
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/events/{eventId}/manage",
            allowed: ["owner", "manager", "admin"]);

        // Event media: any club staff, including volunteers.
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/events/{eventId}/images",
            allowed: ["owner", "manager", "volunteer", "admin"]);
    }

    private static async Task AssertAccessAsync(
        AuthApiTestApp app,
        IReadOnlyDictionary<string, string> callers,
        HttpMethod method,
        string path,
        string[] allowed)
    {
        foreach (var (caller, accessToken) in callers)
        {
            var response = await app.Client.SendAsync(CreateAuthorizedRequest(method, path, accessToken));
            var because = $"{caller} calling {method} {path}: {await app.DescribeFailureAsync(response)}";

            if (allowed.Contains(caller))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK, because);
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, because);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            payload.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            payload.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("FORBIDDEN", because);
        }
    }

    private static async Task AssertEnvelopeAsync(HttpResponseMessage response, string code, string message)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.RootElement.GetProperty("message").GetString().Should().Be(message);
        payload.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    }

    private static async Task<AuthenticatedSessionResponse> CreateSessionAsync(
        AuthApiTestApp app,
        string email,
        string role)
    {
        if (role != "Admin")
        {
            return await app.SignUpAndVerifyByTokenAsync(
                email,
                role: role,
                transport: SessionTransportResolver.ApiValue);
        }

        // Admin cannot self-register, so it is seeded and logged in. Keep this to one login per
        // test: the shared client keeps the trusted-device cookie from that login, and the cookie
        // takes precedence over the header on any later login.
        var user = await app.SeedUserAsync(email, role: role);
        var device = $"{user.Username}-device";
        await app.SeedKnownDeviceAsync(user.Id, device);
        return await app.LoginApiAsync(user.Username!, trustedDeviceToken: device);
    }

    private static async Task<int> CreateClubAsync(AuthApiTestApp app, string accessToken, string name)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/clubs",
            accessToken,
            JsonContent.Create(new
            {
                Name = name,
                Description = "Authorization matrix club",
                Clubtype = "social",
                ClubImageUrl = await app.CreateClubImageUrlAsync(accessToken),
                Email = $"{name.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()}@example.com"
            })));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(response));
        await app.ReindexClubsAsync();

        return (await app.ReadApiResponseAsync<IdModel>(response)).Data!.Id;
    }

    private static async Task<int> CreatePublishedEventAsync(AuthApiTestApp app, string accessToken, int clubId, string name)
    {
        var image = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/events/images/presigned-url",
            accessToken,
            JsonContent.Create(new { clubId, fileName = "poster.png", contentType = "image/png" })));
        image.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(image));
        var imageUrl = (await app.ReadApiResponseAsync<PresignedUrlModel>(image)).Data!.PublicUrl;

        var created = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{clubId}",
            accessToken,
            JsonContent.Create(new
            {
                name,
                description = "An event used to exercise the authorization matrix end to end.",
                location = "Student Center",
                imageUrls = new[] { imageUrl },
                isPrivate = false,
                maxParticipants = 30,
                registerCost = 0,
                startTime = DateTime.UtcNow.AddDays(6),
                endTime = DateTime.UtcNow.AddDays(6).AddHours(2),
                category = EventCategory.Other,
                venueName = "Room A",
                city = "Toronto",
                tags = new[] { "authz" }
            })));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await app.DescribeFailureAsync(created));
        var eventId = (await app.ReadApiResponseAsync<IdModel>(created)).Data!.Id;

        var publish = await app.Client.SendAsync(CreateAuthorizedRequest(
            HttpMethod.Post,
            $"/api/events/{eventId}/publish",
            accessToken,
            JsonContent.Create(new { })));
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(publish));
        await app.ReindexEventsAsync();

        return eventId;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;
        return request;
    }

    private sealed class IdModel
    {
        public int Id { get; set; }
    }

    private sealed class PresignedUrlModel
    {
        public string PublicUrl { get; set; } = string.Empty;
    }
}
