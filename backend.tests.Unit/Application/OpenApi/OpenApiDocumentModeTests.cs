using backend.main.application.openapi;

using FluentAssertions;

using backend.tests.Unit.Support;

using Microsoft.Extensions.Hosting;

using Moq;

namespace backend.tests.Unit.Application.OpenApi;

[Collection(EnvironmentVariableTestCollection.Name)]
public class OpenApiDocumentModeTests
{
    [Fact]
    public void ShouldExposeRuntimeDocuments_ShouldBeTrue_ForDevelopmentEnvironment()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Development);

        var result = OpenApiDocumentMode.ShouldExposeRuntimeDocuments(environment.Object);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldExposeRuntimeDocuments_ShouldBeTrue_WhenExportModeEnabled()
    {
        var original = Environment.GetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable);
        Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, "true");
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Production);

        try
        {
            var result = OpenApiDocumentMode.ShouldExposeRuntimeDocuments(environment.Object);

            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, original);
        }
    }

    [Fact]
    public void ShouldExposeRuntimeDocuments_ShouldBeFalse_ForProductionWithoutExportMode()
    {
        var original = Environment.GetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable);
        Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, "false");
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Production);

        try
        {
            var result = OpenApiDocumentMode.ShouldExposeRuntimeDocuments(environment.Object);

            result.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, original);
        }
    }

    [Fact]
    public void ShouldSkipStartupSideEffects_ShouldTrackExportMode()
    {
        var original = Environment.GetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable);
        Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, "true");

        try
        {
            OpenApiDocumentMode.IsExportMode.Should().BeTrue();
            OpenApiDocumentMode.ShouldSkipStartupSideEffects.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, original);
        }
    }
}
