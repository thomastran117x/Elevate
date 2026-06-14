using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using backend.main.application.openapi;
using backend.main.application.security;
using backend.main.shared.responses;
using backend.main.utilities;

using FluentAssertions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;

namespace backend.tests.Unit.Application.OpenApi;

public class OpenApiConfigurationTests
{
    [Fact]
    public async Task ApplyDocumentTransformAsync_ShouldPopulateInfo_SecurityScheme_AndTags()
    {
        var document = new OpenApiDocument();

        await InvokeStaticAsync(
            "ApplyDocumentTransformAsync",
            document,
            null,
            CancellationToken.None);

        document.Info.Title.Should().Be("EventXperience Backend API");
        document.Info.Version.Should().Be(OpenApiDocumentMode.DocumentName);
        document.Info.Description.Should().Contain("ApiResponse envelope");
        document.Servers.Should().BeEmpty();
        document.Components.Should().NotBeNull();
        document.Components!.SecuritySchemes.Should().ContainKey("bearerAuth");
        document.Components.SecuritySchemes["bearerAuth"].Scheme.Should().Be("bearer");
        document.Components.SecuritySchemes["bearerAuth"].BearerFormat.Should().Be("JWT");
        document.Tags.Select(tag => tag.Name).Should().Equal("auth", "clubs", "events", "payments", "users", "admin");
    }

    [Fact]
    public void ApplyAuthorization_ShouldAddBearerRequirement_OnlyForAuthorizedEndpoints()
    {
        var authorizedOperation = new OpenApiOperation();
        var anonymousOperation = new OpenApiOperation();
        var noAuthOperation = new OpenApiOperation();

        InvokeStatic(
            "ApplyAuthorization",
            authorizedOperation,
            new List<object> { new AuthorizeAttribute() });
        InvokeStatic(
            "ApplyAuthorization",
            anonymousOperation,
            new List<object> { new AuthorizeAttribute(), new AllowAnonymousAttribute() });
        InvokeStatic(
            "ApplyAuthorization",
            noAuthOperation,
            new List<object>());

        authorizedOperation.Security.Should().NotBeNull();
        authorizedOperation.Security.Should().ContainSingle();
        authorizedOperation.Security![0].Keys.Single().Reference!.Id.Should().Be("bearerAuth");
        anonymousOperation.Security.Should().BeEmpty();
        noAuthOperation.Security.Should().BeEmpty();
    }

    [Fact]
    public void ApplyStandardErrors_ShouldAddDefaultResponses_AndProtectedAuthResponses()
    {
        var operation = new OpenApiOperation();

        InvokeStatic(
            "ApplyStandardErrors",
            operation,
            new List<object> { new AuthorizeAttribute() });

        operation.Responses.Should().ContainKeys("400", "401", "403", "500");
        operation.Responses["400"].Content["application/json"].Schema.Reference!.Id
            .Should().Be("ApiResponseOfObject");
        operation.Responses["401"].Description.Should().Contain("Authentication is required");
        operation.Responses["403"].Description.Should().Contain("does not have permission");
    }

    [Fact]
    public void ApplyCsrfDocumentation_ShouldAddHeader_ForProtectedPaths_WithoutDuplicates()
    {
        var protectedOperation = new OpenApiOperation();
        var unprotectedOperation = new OpenApiOperation();

        InvokeStatic("ApplyCsrfDocumentation", protectedOperation, "/api/auth/login");
        InvokeStatic("ApplyCsrfDocumentation", protectedOperation, "/api/auth/login");
        InvokeStatic("ApplyCsrfDocumentation", unprotectedOperation, "/api/events");

        protectedOperation.Parameters.Should().ContainSingle(parameter =>
            parameter.Name == CsrfConfiguration.CsrfHeaderName
            && parameter.In == ParameterLocation.Header
            && parameter.Required);
        unprotectedOperation.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void ApplySpecialHeaders_ShouldAddExpectedHeaders_ForPaymentAndApiAuthRoutes()
    {
        var paymentOperation = new OpenApiOperation();
        var webhookOperation = new OpenApiOperation();
        var refreshOperation = new OpenApiOperation();

        InvokeStatic("ApplySpecialHeaders", paymentOperation, "/api/payments/{eventId}");
        InvokeStatic("ApplySpecialHeaders", webhookOperation, "/api/payments/webhook");
        InvokeStatic("ApplySpecialHeaders", refreshOperation, "/api/auth/api/refresh");
        InvokeStatic("ApplySpecialHeaders", refreshOperation, "/api/auth/api/refresh");

        paymentOperation.Parameters.Should().ContainSingle(parameter => parameter.Name == "Idempotency-Key");
        webhookOperation.Parameters.Should().ContainSingle(parameter =>
            parameter.Name == "Stripe-Signature" && parameter.Required);
        refreshOperation.Parameters.Select(parameter => parameter.Name)
            .Should().BeEquivalentTo([HttpUtility.RefreshTokenHeaderName, HttpUtility.SessionBindingHeaderName]);
    }

    [Fact]
    public void ApplySpecialResponses_AndDescriptions_ShouldDocumentSpecialRoutes()
    {
        var verifyOperation = new OpenApiOperation();
        var deviceVerifyOperation = new OpenApiOperation();
        var csrfOperation = new OpenApiOperation();
        var refreshOperation = new OpenApiOperation();
        var apiRefreshOperation = new OpenApiOperation();
        var webhookOperation = new OpenApiOperation();
        var defaultOperation = new OpenApiOperation();

        InvokeStatic("ApplySpecialResponses", verifyOperation, "/api/auth/verify");
        InvokeStatic("ApplySpecialResponses", deviceVerifyOperation, "/api/auth/device/verify");
        InvokeStatic("ApplyOperationDescriptions", csrfOperation, "/api/auth/csrf");
        InvokeStatic("ApplyOperationDescriptions", refreshOperation, "/api/auth/refresh");
        InvokeStatic("ApplyOperationDescriptions", apiRefreshOperation, "/api/auth/api/refresh");
        InvokeStatic("ApplyOperationDescriptions", webhookOperation, "/api/payments/webhook");
        InvokeStatic("ApplyOperationDescriptions", defaultOperation, "/api/events");

        verifyOperation.Responses.Should().ContainKey("302");
        deviceVerifyOperation.Responses.Should().ContainKey("302");
        csrfOperation.Description.Should().Contain("CSRF token");
        refreshOperation.Description.Should().Contain("Browser-cookie session refresh");
        apiRefreshOperation.Description.Should().Contain("API-token session refresh");
        webhookOperation.Description.Should().Contain("Stripe-Signature");
        defaultOperation.Description.Should().BeNull();
    }

    [Fact]
    public void AddResponseIfMissing_ShouldAddSchemaReference_AndAvoidDuplicates()
    {
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses()
        };

        InvokeStatic(
            "AddResponseIfMissing",
            operation,
            "400",
            "Bad request",
            typeof(ApiResponse<object?>));
        InvokeStatic(
            "AddResponseIfMissing",
            operation,
            "400",
            "Duplicate",
            typeof(ApiResponse<object?>));

        operation.Responses.Should().ContainSingle();
        operation.Responses["400"].Description.Should().Be("Bad request");
        operation.Responses["400"].Content["application/json"].Schema.Reference!.Id
            .Should().Be("ApiResponseOfObject");
    }

    [Fact]
    public void SchemaReferenceHelpers_ShouldHandleNullable_Arrays_AndGenerics()
    {
        InvokeStatic<string>("GetSchemaReferenceId", typeof(int?)).Should().Be("Int32");
        InvokeStatic<string>("GetSchemaReferenceId", typeof(string[])).Should().Be("StringArray");
        InvokeStatic<string>("GetSchemaReferenceId", typeof(Dictionary<string, ApiResponse<object?>>))
            .Should().Be("DictionaryOfStringAndApiResponseOfObject");
        InvokeStatic<string>("GetSchemaReferenceId", typeof(object)).Should().Be("Object");
    }

    [Fact]
    public void CreateSchemaReferenceId_ShouldUseJsonTypeInfoType()
    {
        var jsonTypeInfo = JsonSerializerOptions.Default.GetTypeInfo(typeof(List<string>));

        InvokeStatic<string>("CreateSchemaReferenceId", jsonTypeInfo).Should().Be("ListOfString");
    }

    [Fact]
    public void PathAndTokenHelpers_ShouldNormalizeAndSanitizeValues()
    {
        InvokeStatic<string>("NormalizeRelativePath", new object?[] { null }).Should().Be("/");
        InvokeStatic<string>("NormalizeRelativePath", "   ").Should().Be("/");
        InvokeStatic<string>("NormalizeRelativePath", "api/auth/login").Should().Be("/api/auth/login");
        InvokeStatic<string>("SanitizeToken", "Auth-Controller!").Should().Be("AuthController");
        InvokeStatic<string>("SanitizeToken", "   ").Should().Be("operation");
        InvokeStatic<bool>("IsSupportedDocumentName", "v1").Should().BeTrue();
        InvokeStatic<bool>("IsSupportedDocumentName", "V1").Should().BeTrue();
        InvokeStatic<bool>("IsSupportedDocumentName", "v2").Should().BeFalse();
    }

    [Fact]
    public void AddHeaderIfMissing_ShouldAvoidDuplicates()
    {
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = HttpUtility.RefreshTokenHeaderName,
                    In = ParameterLocation.Header
                }
            ]
        };

        InvokeStatic(
            "AddHeaderIfMissing",
            operation,
            HttpUtility.RefreshTokenHeaderName,
            "duplicate");
        InvokeStatic(
            "AddHeaderIfMissing",
            operation,
            HttpUtility.SessionBindingHeaderName,
            "binding");

        operation.Parameters.Should().HaveCount(2);
        operation.Parameters.Should().Contain(parameter =>
            parameter.Name == HttpUtility.SessionBindingHeaderName
            && parameter.Description == "binding");
    }

    [Fact]
    public void AddResponseIfMissing_ShouldSupportDescriptionOnlyResponses()
    {
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses()
        };

        InvokeStatic("AddResponseIfMissing", operation, "302", "Redirect only", null);

        operation.Responses.Should().ContainKey("302");
        operation.Responses["302"].Description.Should().Be("Redirect only");
        operation.Responses["302"].Content.Should().BeEmpty();
    }

    private static object? InvokeStatic(string methodName, params object?[] arguments)
    {
        var method = typeof(OpenApiConfiguration).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return method.Invoke(null, arguments);
    }

    private static T InvokeStatic<T>(string methodName, params object?[] arguments)
    {
        return (T)InvokeStatic(methodName, arguments)!;
    }

    private static async Task InvokeStaticAsync(string methodName, params object?[] arguments)
    {
        var task = (Task)InvokeStatic(methodName, arguments)!;
        await task;
    }
}
