using backend.main.application.environment;

using FluentAssertions;

using backend.tests.Unit.Support;

namespace backend.tests.Unit.Application.EnvironmentConfig;

[Collection(EnvironmentVariableTestCollection.Name)]
public class EnvFileLoaderTests
{
    [Fact]
    public void FindEnvFiles_ShouldReturnRootBeforeNearest()
    {
        var rootDir = CreateTempDirectory();
        var childDir = Directory.CreateDirectory(Path.Combine(rootDir.FullName, "backend", "bin"));
        File.WriteAllText(Path.Combine(rootDir.FullName, ".env"), "ROOT_KEY=root");
        File.WriteAllText(Path.Combine(rootDir.FullName, "backend", ".env"), "CHILD_KEY=child");

        try
        {
            var envFiles = EnvFileLoader.FindEnvFiles(childDir.FullName);

            envFiles.Should().ContainInOrder(
                Path.Combine(rootDir.FullName, ".env"),
                Path.Combine(rootDir.FullName, "backend", ".env"));
        }
        finally
        {
            rootDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadFile_ShouldSkipBlankAssignments_AndKeepExistingValue()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["SMTP_SERVER"] = null
        });

        var rootDir = CreateTempDirectory();
        var envPath = Path.Combine(rootDir.FullName, ".env");
        File.WriteAllLines(envPath,
        [
            "SMTP_SERVER=smtp.gmail.com",
            "EMAIL_USER=mailer@example.com",
            "EMPTY_VALUE=",
            "# COMMENT=ignored"
        ]);

        try
        {
            EnvFileLoader.LoadFile(envPath);

            Environment.GetEnvironmentVariable("SMTP_SERVER").Should().Be("smtp.gmail.com");
            Environment.GetEnvironmentVariable("EMAIL_USER").Should().Be("mailer@example.com");
            Environment.GetEnvironmentVariable("EMPTY_VALUE").Should().BeNull();
            Environment.GetEnvironmentVariable("COMMENT").Should().BeNull();
        }
        finally
        {
            rootDir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("KEY=value", "KEY", "value")]
    [InlineData(" KEY = value # trailing comment", "KEY", "value")]
    [InlineData("export QUOTED=\" spaced value \"", "QUOTED", " spaced value ")]
    public void TryParseAssignment_ShouldHandleSupportedEnvSyntax(string line, string expectedKey, string expectedValue)
    {
        var parsed = EnvFileLoader.TryParseAssignment(line, out var key, out var value);

        parsed.Should().BeTrue();
        key.Should().Be(expectedKey);
        value.Should().Be(expectedValue);
    }

    private static DirectoryInfo CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"env-loader-tests-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                _originals[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
