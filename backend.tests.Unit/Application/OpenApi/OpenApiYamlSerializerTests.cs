using System.Reflection;
using System.Runtime.ExceptionServices;

using FluentAssertions;

namespace backend.tests.Unit.Application.OpenApi;

public class OpenApiYamlSerializerTests
{
    [Fact]
    public void ConvertJsonDocumentToYaml_ShouldRenderNestedObjectsArraysAndScalars()
    {
        const string json = """
            {
              "openapi": "3.1.0",
              "info": {
                "title": "Event API",
                "slug": "event-api",
                "description": "O'Hara docs",
                "notes": "",
                "nullable": null,
                "emptyObject": {},
                "emptyArray": []
              },
              "servers": [
                {
                  "url": "https://api.test",
                  "description": "Primary server"
                },
                {
                  "metadata": {
                    "region": "ca-central"
                  },
                  "name": "backup"
                }
              ],
              "matrix": [
                [1, 2],
                []
              ]
            }
            """;

        var yaml = ConvertJsonDocumentToYaml(json);

        Assert.Equal(
            NormalizeLineEndings(
            """
            openapi: 3.1.0
            info:
              title: 'Event API'
              slug: event-api
              description: 'O''Hara docs'
              notes: ''
              nullable: null
              emptyObject: {}
              emptyArray: []
            servers:
              - url: 'https://api.test'
                description: 'Primary server'
              - metadata:
                  region: ca-central
                name: backup
            matrix:
              -
                - 1
                - 2
              - []

            """),
            NormalizeLineEndings(yaml));
    }

    [Fact]
    public void ConvertJsonDocumentToYaml_ShouldEscapeKeysAndPreserveBooleans()
    {
        const string json = """
            {
              "has space": true,
              "simple-key": false,
              "child": {
                "owner's note": "keep me"
              }
            }
            """;

        var yaml = ConvertJsonDocumentToYaml(json);

        Assert.Equal(
            NormalizeLineEndings(
            """
            'has space': true
            simple-key: false
            child:
              'owner''s note': 'keep me'

            """),
            NormalizeLineEndings(yaml));
    }

    [Fact]
    public void ConvertJsonDocumentToYaml_ShouldThrow_WhenRootNodeIsNull()
    {
        var action = () => ConvertJsonDocumentToYaml("null");

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("OpenAPI JSON output could not be parsed.");
    }

    [Theory]
    [InlineData("{}", "{}\n")]
    [InlineData("[]", "[]\n")]
    [InlineData("42", "42\n")]
    public void ConvertJsonDocumentToYaml_ShouldHandleEmptyAndScalarRoots(string json, string expectedYaml)
    {
        var yaml = ConvertJsonDocumentToYaml(json);

        NormalizeLineEndings(yaml).Should().Be(expectedYaml);
    }

    [Fact]
    public void ConvertJsonDocumentToYaml_ShouldRenderNullAndEmptyArrayItems()
    {
        const string json = """
            [
              null,
              {},
              []
            ]
            """;

        var yaml = ConvertJsonDocumentToYaml(json);

        NormalizeLineEndings(yaml).Should().Be(
            NormalizeLineEndings(
            """
            - null
            - {}
            - []

            """));
    }

    private static string ConvertJsonDocumentToYaml(string json)
    {
        var type = Type.GetType("backend.main.application.openapi.OpenApiYamlSerializer, backend")
            ?? throw new InvalidOperationException("OpenApiYamlSerializer type was not found.");
        var method = type.GetMethod(
            "ConvertJsonDocumentToYaml",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("ConvertJsonDocumentToYaml method was not found.");

        try
        {
            return (string)method.Invoke(null, [json])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n");
}
