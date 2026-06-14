using backend.main.application.openapi;

using FluentAssertions;

namespace backend.tests.Unit.Application.OpenApi;

public class OpenApiDocumentModeTests
{
    [Fact]
    public void ShouldSkipStartupSideEffects_ShouldBeFalse_WhenExportModeDisabled()
    {
        var original = Environment.GetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable);
        Environment.SetEnvironmentVariable(OpenApiDocumentMode.ExportEnvironmentVariable, "false");

        try
        {
            OpenApiDocumentMode.IsExportMode.Should().BeFalse();
            OpenApiDocumentMode.ShouldSkipStartupSideEffects.Should().BeFalse();
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
