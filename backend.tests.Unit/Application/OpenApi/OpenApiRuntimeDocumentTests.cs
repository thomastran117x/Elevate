using System.Net;
using System.Text.Json;

using backend.main.application.openapi;

using FluentAssertions;

using backend.tests.Unit.Support;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Unit.Application.OpenApi;

[Collection(EnvironmentVariableTestCollection.Name)]
public class OpenApiRuntimeDocumentTests
{
    [Fact]
    public async Task OpenApiRoutes_ShouldServeRuntimeDocuments_AndExcludeOpenApiPaths()
    {
        await using var host = await OpenApiTestHost.StartAsync();

        var jsonResponse = await host.Client.GetAsync("/openapi.json");
        var yamlResponse = await host.Client.GetAsync("/openapi.yaml");
        var notFoundResponse = await host.Client.GetAsync("/openapi/v2.json");

        jsonResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        yamlResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        notFoundResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var json = JsonDocument.Parse(await jsonResponse.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/events", out _).Should().BeTrue();
        paths.TryGetProperty("/openapi/internal", out _).Should().BeFalse();
    }

    [Fact]
    public async Task OpenApiRoutes_ShouldRespectExactIncludePrefix()
    {
        await using var host = await OpenApiTestHost.StartAsync("=/api/events");

        using var json = JsonDocument.Parse(await host.Client.GetStringAsync("/openapi.json"));
        var paths = json.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/events", out _).Should().BeTrue();
        paths.TryGetProperty("/api/auth/login", out _).Should().BeFalse();
        paths.TryGetProperty("/api/clubs", out _).Should().BeFalse();
    }

    [Fact]
    public async Task OpenApiRoutes_ShouldRespectWildcardAndPrefixIncludeFilters()
    {
        await using var wildcardHost = await OpenApiTestHost.StartAsync("*auth*");
        using var wildcardJson = JsonDocument.Parse(
            await wildcardHost.Client.GetStringAsync("/openapi.json")
        );
        var wildcardPaths = wildcardJson.RootElement.GetProperty("paths");

        wildcardPaths.TryGetProperty("/api/auth/login", out _).Should().BeTrue();
        wildcardPaths.TryGetProperty("/api/events", out _).Should().BeFalse();

        await using var prefixHost = await OpenApiTestHost.StartAsync("/api/clubs");
        using var prefixJson = JsonDocument.Parse(
            await prefixHost.Client.GetStringAsync("/openapi.json")
        );
        var prefixPaths = prefixJson.RootElement.GetProperty("paths");

        prefixPaths.TryGetProperty("/api/clubs", out _).Should().BeTrue();
        prefixPaths.TryGetProperty("/api/events", out _).Should().BeFalse();
    }

    [Fact]
    public async Task OpenApiRoutes_ShouldApplyOperationTags_Descriptions_AndSecurityMetadata()
    {
        await using var host = await OpenApiTestHost.StartAsync();

        using var json = JsonDocument.Parse(await host.Client.GetStringAsync("/openapi.json"));
        var paths = json.RootElement.GetProperty("paths");

        var authOperation = paths.GetProperty("/api/auth/login").GetProperty("post");
        authOperation.GetProperty("tags")[0].GetString().Should().Be("auth");
        authOperation.GetProperty("parameters")
            .EnumerateArray()
            .Any(parameter =>
                parameter.GetProperty("name").GetString()
                == backend.main.application.security.CsrfConfiguration.CsrfHeaderName
            )
            .Should()
            .BeTrue();
        authOperation.TryGetProperty("security", out _).Should().BeTrue();

        var paymentOperation = paths.GetProperty("/api/payments/{eventId}").GetProperty("get");
        paymentOperation.GetProperty("tags")[0].GetString().Should().Be("payments");
        paymentOperation.GetProperty("parameters")
            .EnumerateArray()
            .Any(parameter => parameter.GetProperty("name").GetString() == "Idempotency-Key")
            .Should()
            .BeTrue();

        var adminOperation = paths.GetProperty("/api/admin/stats").GetProperty("get");
        adminOperation.GetProperty("tags")[0].GetString().Should().Be("admin");

        var userOperation = paths.GetProperty("/api/users/profile").GetProperty("get");
        userOperation.GetProperty("tags")[0].GetString().Should().Be("users");

        var eventOperation = paths.GetProperty("/api/events").GetProperty("get");
        eventOperation.GetProperty("tags")[0].GetString().Should().Be("events");
    }

    private sealed class OpenApiTestHost : IAsyncDisposable
    {
        private readonly string? _originalIncludePrefix;

        private OpenApiTestHost(WebApplication app, HttpClient client, string? originalIncludePrefix)
        {
            App = app;
            Client = client;
            _originalIncludePrefix = originalIncludePrefix;
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }

        public static async Task<OpenApiTestHost> StartAsync(string? includePrefix = null)
        {
            var originalIncludePrefix = Environment.GetEnvironmentVariable(
                OpenApiDocumentMode.IncludePrefixEnvironmentVariable
            );

            Environment.SetEnvironmentVariable(
                OpenApiDocumentMode.IncludePrefixEnvironmentVariable,
                includePrefix
            );

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddAuthorization();
            builder.Services.AddAppOpenApi();

            var app = builder.Build();

            app.MapGet("/api/admin/stats", () => Results.Ok(new { ok = true }));
            app.MapGet("/api/events", () => Results.Ok(new { ok = true }));
            app.MapGet("/api/clubs", () => Results.Ok(new { ok = true }));
            app.MapGet("/api/payments/{eventId}", (string eventId) => Results.Ok(eventId));
            app.MapGet("/api/users/profile", () => Results.Ok(new { ok = true }));
            app.MapGet("/openapi/internal", () => Results.Ok("hidden"));
            app.MapPost("/api/auth/login", () => Results.Ok("login")).RequireAuthorization();
            app.MapAppOpenApi();

            await app.StartAsync();
            return new OpenApiTestHost(app, app.GetTestClient(), originalIncludePrefix);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
            Environment.SetEnvironmentVariable(
                OpenApiDocumentMode.IncludePrefixEnvironmentVariable,
                _originalIncludePrefix
            );
        }
    }

}
