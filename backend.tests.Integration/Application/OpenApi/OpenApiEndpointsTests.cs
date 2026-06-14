using System.Net;
using System.Text.Json;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

namespace backend.tests.Integration.Application.OpenApi;

public class OpenApiEndpointsTests
{
    [Fact]
    public async Task OpenApiJsonRoutes_ShouldServeTheGeneratedDocument()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var defaultResponse = await app.Client.GetAsync("/openapi.json");
        var versionedResponse = await app.Client.GetAsync("/openapi/v1.json");

        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        versionedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        versionedResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        using var json = JsonDocument.Parse(await defaultResponse.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.");
        json.RootElement.GetProperty("info").GetProperty("title").GetString()
            .Should().Be("EventXperience Backend API");
    }

    [Fact]
    public async Task OpenApiYamlRoutes_ShouldServeTheGeneratedDocument()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var defaultResponse = await app.Client.GetAsync("/openapi.yaml");
        var versionedResponse = await app.Client.GetAsync("/openapi/v1.yaml");

        defaultResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        versionedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/yaml");
        versionedResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/yaml");

        var yaml = await defaultResponse.Content.ReadAsStringAsync();
        yaml.Should().Contain("openapi:");
        yaml.Should().Contain("EventXperience Backend API");
    }

    [Fact]
    public async Task UnsupportedDocumentNames_ShouldReturnNotFound()
    {
        await using var app = await AuthApiTestApp.CreateAsync();

        var jsonResponse = await app.Client.GetAsync("/openapi/v2.json");
        var yamlResponse = await app.Client.GetAsync("/openapi/v2.yaml");

        jsonResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        yamlResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
