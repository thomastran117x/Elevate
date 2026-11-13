using FluentAssertions;

namespace backend.tests.Unit.Application.Security.Authorization;

/// <summary>
/// Keeps the matrix in docs/AUTHORIZATION.md identical to the code that enforces it. Regenerate
/// it with <c>UPDATE_AUTHORIZATION_DOC=1</c> (see the document's "Regenerating the matrix").
/// </summary>
public class AuthorizationDocumentTests
{
    [Fact]
    public void AuthorizationDoc_ShouldMatchTheGeneratedMatrix()
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "AUTHORIZATION.md");
        var document = File.ReadAllText(path).ReplaceLineEndings("\n");
        var begin = document.IndexOf(AuthorizationMatrixDocument.BeginMarker, StringComparison.Ordinal);
        var end = document.IndexOf(AuthorizationMatrixDocument.EndMarker, StringComparison.Ordinal);

        begin.Should().BeGreaterThanOrEqualTo(0, "docs/AUTHORIZATION.md must contain the generated-section markers");
        end.Should().BeGreaterThan(begin);

        var committed = document[begin..(end + AuthorizationMatrixDocument.EndMarker.Length)] + "\n";
        var generated = AuthorizationMatrixDocument.Render();

        if (Environment.GetEnvironmentVariable(AuthorizationMatrixDocument.RegenerateVariable) == "1")
        {
            File.WriteAllText(path, document[..begin] + generated + document[(end + AuthorizationMatrixDocument.EndMarker.Length + 1)..]);
            return;
        }

        committed.Should().Be(
            generated,
            "the permission matrix in docs/AUTHORIZATION.md is generated; rerun this test with {0}=1 to update it",
            AuthorizationMatrixDocument.RegenerateVariable);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root (backend.sln) above the test output.");
    }
}
