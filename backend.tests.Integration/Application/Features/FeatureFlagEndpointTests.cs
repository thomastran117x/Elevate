using System.Net;
using System.Text.Json;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Application.Features;

public class FeatureFlagEndpointTests
{
    [Fact]
    public async Task DisabledAuthEndpoints_ShouldReturnStructuredNotFoundPayload()
    {
        await using var app = await AuthApiTestApp.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["FeatureFlags:auth"] = "false"
            });

        var response = await app.Client.GetAsync("/api/auth/csrf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("message").GetString().Should().Be("Resource not found.");
        payload.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("RESOURCE_NOT_FOUND");
        payload.RootElement.GetProperty("error").GetProperty("details").GetProperty("path").GetString()
            .Should().Be("/api/auth/csrf");
    }

    [Fact]
    public async Task DisabledAuthEndpoints_ShouldBeAbsentFromOpenApi()
    {
        await using var app = await AuthApiTestApp.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["FeatureFlags:auth"] = "false"
            });

        var response = await app.Client.GetAsync("/openapi.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("paths").TryGetProperty("/api/auth/login", out _).Should().BeFalse();
        document.RootElement.GetProperty("paths").TryGetProperty("/api/auth/csrf", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DisabledInvitationSubfeature_ShouldHideInvitationEndpoints_WithoutBreakingEventsSearch()
    {
        await using var app = await AuthApiTestApp.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["FeatureFlags:events.invitations"] = "false"
            });

        var invitationResponse = await app.Client.GetAsync("/api/events/me/invited");
        var eventsResponse = await app.Client.GetAsync("/api/events?page=1&pageSize=20");

        invitationResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        eventsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await (await app.Client.GetAsync("/openapi.json")).Content.ReadAsStringAsync());
        document.RootElement.GetProperty("paths").TryGetProperty("/api/events/me/invited", out _).Should().BeFalse();
        document.RootElement.GetProperty("paths").TryGetProperty("/api/events", out _).Should().BeTrue();
    }
}
