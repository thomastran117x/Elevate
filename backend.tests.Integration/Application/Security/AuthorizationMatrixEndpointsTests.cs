using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.application.security;
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

        var endpoints = RuntimeEndpointAuthorizationAudit.Audit(app.Endpoints);
        var act = () => RuntimeEndpointAuthorizationAudit.AssertEveryEndpointIsClassified(endpoints);

        act.Should().NotThrow("controllers, hubs, and minimal endpoints must each opt in or out explicitly");
    }

    [Fact]
    public async Task EveryProtectedEndpoint_ShouldChallengeAnonymousCallers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var protectedEndpoints = RoutedEndpoints(app)
            .Where(endpoint => endpoint.Access == RuntimeEndpointAccess.Protected)
            .ToArray();
        var failures = new List<string>();
        // Endpoints with [ValidateAntiForgeryToken] refuse an unsafe request without the CSRF pair
        // (400) before authentication challenges, so probes carry a valid pair, as a browser does,
        // to isolate the authentication answer. The client keeps the antiforgery cookie.
        var csrfToken = await app.GetCsrfTokenAsync();

        foreach (var endpoint in protectedEndpoints)
        {
            // No body or token: authorization must answer before binding or the action.
            var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path);
            if (endpoint.Method != "GET")
                request.Headers.Add(CsrfConfiguration.CsrfHeaderName, csrfToken);
            var response = await app.Client.SendAsync(request);

            if (!await IsAuthenticationChallengeAsync(response))
                failures.Add($"{endpoint.Key} -> {(int)response.StatusCode} {await app.DescribeFailureAsync(response)}");
        }

        protectedEndpoints.Should().HaveCountGreaterThan(100, "the gate should probe the whole protected surface");
        failures.Should().BeEmpty("every protected endpoint answers an anonymous caller with the 401 envelope");
    }

    [Fact]
    public async Task EveryPublicEndpoint_ShouldBeReachableWithoutAToken()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var publicEndpoints = RoutedEndpoints(app)
            .Where(endpoint => endpoint.Access == RuntimeEndpointAccess.Anonymous)
            .ToArray();
        var failures = new List<string>();

        foreach (var endpoint in publicEndpoints)
        {
            var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path);
            if (endpoint.Method is "POST" or "PUT" or "PATCH")
                request.Content = JsonContent.Create(new { });

            // A public endpoint may refuse the empty probe (400, 403 for CSRF, 404), and some answer
            // their own 401s, but never the authentication challenge that protects everything else.
            var response = await app.Client.SendAsync(request);
            if (await IsAuthenticationChallengeAsync(response))
                failures.Add($"{endpoint.Key} -> challenged");
        }

        publicEndpoints.Should().Contain(endpoint => endpoint.Key == "POST /api/auth/login");
        failures.Should().BeEmpty("a reviewed public endpoint must not require a token");
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

        // Owner powers: the owner and, explicitly, admins (#331). Managers are refused.
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/clubs/{clubId}/staff",
            allowed: ["owner", "admin"]);

        // Club management: owner, managers, and admins.
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/events/clubs/{clubId}/manage",
            allowed: ["owner", "manager", "admin"]);
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/events/{eventId}/manage",
            allowed: ["owner", "manager", "admin"]);

        // Event media: any club staff, including volunteers.
        await AssertAccessAsync(app, callers, HttpMethod.Get, $"/api/events/{eventId}/images",
            allowed: ["owner", "manager", "volunteer", "admin"]);
    }

    [Fact]
    public async Task ClubOwnerPowers_ShouldBeHeldByTheOwnerAndAdminsButNotManagers()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "authz-own-owner@example.com", "Organizer");
        var manager = await CreateSessionAsync(app, "authz-own-manager@example.com", "Participant");
        await CreateSessionAsync(app, "authz-own-helper@example.com", "Participant");
        var admin = await CreateSessionAsync(app, "authz-own-admin@example.com", "Admin");

        var clubId = await CreateClubAsync(app, owner.AccessToken, "Authz Owner Powers Club");
        var ownerUser = (await app.FindUserByEmailAsync("authz-own-owner@example.com"))!;
        var managerUser = (await app.FindUserByEmailAsync("authz-own-manager@example.com"))!;
        var helperUser = (await app.FindUserByEmailAsync("authz-own-helper@example.com"))!;
        await app.AddClubStaffAsync(clubId, managerUser.Id, ownerUser.Id, ClubStaffRole.Manager);

        // A manager runs the club but holds none of the owner's powers; the edge refuses before
        // the service runs, with the same messages the services use.
        await AssertRefusedAsync(app, manager.AccessToken, HttpMethod.Get, $"/api/clubs/{clubId}/staff", "Not allowed");
        await AssertRefusedAsync(app, manager.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/staff/volunteers",
            "Not allowed", new { userId = helperUser.Id });
        await AssertRefusedAsync(app, manager.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/transfer-ownership",
            "Not allowed", new { newOwnerUserId = managerUser.Id });
        await AssertRefusedAsync(app, manager.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/staff/invitations",
            "Only the club owner can manage staff invitations.", new { identifier = helperUser.Email, role = "Volunteer" });
        await AssertRefusedAsync(app, manager.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubId}", "Not allowed");

        // Admins hold the owner's powers explicitly (#331).
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Get, $"/api/clubs/{clubId}/staff", HttpStatusCode.OK);
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/staff/volunteers",
            HttpStatusCode.Created, new { userId = helperUser.Id });
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubId}/staff/{helperUser.Id}",
            HttpStatusCode.OK);
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/transfer-ownership",
            HttpStatusCode.OK, new { newOwnerUserId = managerUser.Id });
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubId}", HttpStatusCode.OK);
        await AssertStatusAsync(app, admin.AccessToken, HttpMethod.Get, $"/api/clubs/{clubId}/staff", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ClubStaffRoles_ShouldStayInTheirClubAndTakeEffectOnTheNextRequest()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var ownerA = await CreateSessionAsync(app, "authz-iso-owner-a@example.com", "Organizer");
        var ownerB = await CreateSessionAsync(app, "authz-iso-owner-b@example.com", "Organizer");
        var managerB = await CreateSessionAsync(app, "authz-iso-manager-b@example.com", "Participant");

        var clubA = await CreateClubAsync(app, ownerA.AccessToken, "Authz Isolation Club A");
        var clubB = await CreateClubAsync(app, ownerB.AccessToken, "Authz Isolation Club B");
        var ownerBUser = (await app.FindUserByEmailAsync("authz-iso-owner-b@example.com"))!;
        var managerBUser = (await app.FindUserByEmailAsync("authz-iso-manager-b@example.com"))!;
        await app.AddClubStaffAsync(clubB, managerBUser.Id, ownerBUser.Id, ClubStaffRole.Manager);

        await AssertStatusAsync(app, managerB.AccessToken, HttpMethod.Get, $"/api/events/clubs/{clubB}/manage", HttpStatusCode.OK);
        await AssertRefusedAsync(app, managerB.AccessToken, HttpMethod.Get, $"/api/events/clubs/{clubA}/manage", "Not allowed");
        await AssertRefusedAsync(app, managerB.AccessToken, HttpMethod.Get, $"/api/clubs/{clubA}/members/invitations",
            "Only club organisers can manage member invitations.");

        await AssertStatusAsync(app, ownerB.AccessToken, HttpMethod.Delete, $"/api/clubs/{clubB}/staff/{managerBUser.Id}",
            HttpStatusCode.OK);

        // Same access token: staff roles are read from the database on every check.
        await AssertRefusedAsync(app, managerB.AccessToken, HttpMethod.Get, $"/api/events/clubs/{clubB}/manage", "Not allowed");
    }

    [Fact]
    public async Task PrivateClubContent_ShouldBeRefusedBeforeChildResourcesAreLookedUp()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "authz-content-owner@example.com", "Organizer");
        var outsider = await CreateSessionAsync(app, "authz-content-outsider@example.com", "Participant");
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Authz Private Content Club", isPrivate: true);
        const int missingPostId = 987_654;

        // A post that does not exist answers a non-member exactly like one that does, so a private
        // club's post ids cannot be probed. Anonymous readers are challenged with the 401 message.
        var anonymous = await app.Client.GetAsync($"/api/clubs/{clubId}/posts/{missingPostId}/comments");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await app.DescribeFailureAsync(anonymous));
        await AssertEnvelopeAsync(anonymous, "UNAUTHORIZED", "Authentication is required to view comments for a private club.");

        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Get,
            $"/api/clubs/{clubId}/posts/{missingPostId}/comments",
            "You must be a member of this club to view its comments.");
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Get,
            $"/api/clubs/{clubId}/posts/{missingPostId}",
            "You must be a member of this club to view its posts.");

        // Staff-only post writes refuse non-staff before the post is loaded.
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Post, $"/api/clubs/{clubId}/posts",
            "You are not allowed to create posts for this club.", new { title = "Hi", content = "Body" });
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Put, $"/api/clubs/{clubId}/posts/{missingPostId}",
            "You are not allowed to update this post.", new { title = "Hi", content = "Body" });

        // The owner passes the gate and gets the post's own not-found answer.
        await AssertStatusAsync(app, owner.AccessToken, HttpMethod.Get,
            $"/api/clubs/{clubId}/posts/{missingPostId}/comments", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EventManagement_ShouldRefuseOutsidersBeforeRevealingEventState()
    {
        await using var app = await AuthApiTestApp.CreateAsync();
        var owner = await CreateSessionAsync(app, "authz-event-owner@example.com", "Organizer");
        var outsider = await CreateSessionAsync(app, "authz-event-outsider@example.com", "Organizer");
        var outsiderUser = (await app.FindUserByEmailAsync("authz-event-outsider@example.com"))!;
        var clubId = await CreateClubAsync(app, owner.AccessToken, "Authz Event State Club");
        var eventId = await CreatePublishedEventAsync(app, owner.AccessToken, clubId, "Authz Event State Event");

        // Inviting to a public event is a 400 for its managers; an outsider is refused first, so
        // the answer no longer reveals whether the event is private or published.
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Post, $"/api/events/{eventId}/invitations",
            "Not allowed", new { userIds = new[] { outsiderUser.Id } });
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Get, $"/api/events/{eventId}/invitations", "Not allowed");
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Post, $"/api/events/{eventId}/publish", "Not allowed");
        await AssertRefusedAsync(app, outsider.AccessToken, HttpMethod.Get, $"/api/events/clubs/{clubId}/series", "Not allowed");

        // Missing resources keep their usual not-found answer.
        await AssertStatusAsync(app, outsider.AccessToken, HttpMethod.Get, "/api/events/series/987654", HttpStatusCode.NotFound);
        await AssertStatusAsync(app, outsider.AccessToken, HttpMethod.Get, "/api/events/987654/manage", HttpStatusCode.NotFound);
        await AssertStatusAsync(app, owner.AccessToken, HttpMethod.Get, $"/api/events/{eventId}/manage", HttpStatusCode.OK);
    }

    private static async Task AssertRefusedAsync(
        AuthApiTestApp app, string accessToken, HttpMethod method, string path, string message, object? body = null)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            method, path, accessToken, body is null ? null : JsonContent.Create(body)));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await app.DescribeFailureAsync(response));
        await AssertEnvelopeAsync(response, "FORBIDDEN", message);
    }

    private static async Task AssertStatusAsync(
        AuthApiTestApp app, string accessToken, HttpMethod method, string path, HttpStatusCode expected, object? body = null)
    {
        var response = await app.Client.SendAsync(CreateAuthorizedRequest(
            method, path, accessToken, body is null ? null : JsonContent.Create(body)));
        response.StatusCode.Should().Be(expected, $"{method} {path}: {await app.DescribeFailureAsync(response)}");
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

    private static async Task<int> CreateClubAsync(AuthApiTestApp app, string accessToken, string name, bool isPrivate = false)
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
                Email = $"{name.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()}@example.com",
                IsPrivate = isPrivate
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

    private static IEnumerable<ProbedEndpoint> RoutedEndpoints(AuthApiTestApp app) =>
        app.Endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => RuntimeEndpointAuthorizationAudit.Audit(new DefaultEndpointDataSource(endpoint))
                .Select(audited => new ProbedEndpoint(
                    audited.Method,
                    RuntimeEndpointAuthorizationAudit.ProbePath(endpoint.RoutePattern),
                    audited.Access,
                    audited.Key)))
            .DistinctBy(endpoint => endpoint.Key);

    private static async Task<bool> IsAuthenticationChallengeAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized
            || response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            return false;
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.TryGetProperty("error", out var error)
            && error.TryGetProperty("code", out var code)
            && code.GetString() == "UNAUTHORIZED"
            && payload.RootElement.TryGetProperty("message", out var message)
            && message.GetString() == "Missing access token";
    }

    private sealed record ProbedEndpoint(string Method, string Path, RuntimeEndpointAccess Access, string Key);

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
