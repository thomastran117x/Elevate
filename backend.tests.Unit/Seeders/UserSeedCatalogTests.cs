using backend.main.seeders;

using FluentAssertions;

namespace backend.tests.Unit.Seeders;

public class UserSeedCatalogTests
{
    [Fact]
    public void All_ShouldExposeUniqueLoginReadyUsernames()
    {
        var users = UserSeedCatalog.All;
        var usernames = users.Select(user => user.Username).ToList();

        users.Should().HaveCount(42);
        usernames.Should().OnlyContain(username =>
            !string.IsNullOrWhiteSpace(username)
            && username.Length <= 50
            && username.All(character => char.IsLetterOrDigit(character)
                || character == '.'
                || character == '_'
                || character == '-'));
        usernames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().HaveCount(usernames.Count);
    }
}
