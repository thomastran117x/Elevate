using backend.main.features.clubs.posts;

namespace backend.main.seeders;

public sealed record ResolvedSeedClubPostDefinition(
    string Title,
    string Content,
    PostType PostType,
    bool IsPinned,
    SeedClubAuthorRole AuthorRole,
    int LikesCount,
    int ViewCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public static class ThematicClubPostFactory
{
    public static IReadOnlyList<ResolvedSeedClubPostDefinition> BuildPosts(
        SeedClubDefinition club,
        DateTime seasonStartUtc)
    {
        if (club.Posts.Count != 10)
            throw new InvalidOperationException(
                $"Club '{club.Name}' must define exactly 10 seeded posts, but defined {club.Posts.Count}.");

        return club.Posts
            .Select(post => new ResolvedSeedClubPostDefinition(
                Title: ApplyTemplate(post.TitleTemplate, club),
                Content: ApplyTemplate(post.ContentTemplate, club),
                PostType: post.PostType,
                IsPinned: post.IsPinned,
                AuthorRole: post.AuthorRole,
                LikesCount: post.LikesCount,
                ViewCount: post.ViewCount,
                CreatedAtUtc: seasonStartUtc.AddDays(-post.DayOffset),
                UpdatedAtUtc: seasonStartUtc.AddDays(-post.DayOffset + 1)))
            .OrderByDescending(post => post.CreatedAtUtc)
            .ThenBy(post => post.Title, StringComparer.Ordinal)
            .ToList();
    }

    private static string ApplyTemplate(string template, SeedClubDefinition club)
    {
        return template
            .Replace("{club}", club.Name, StringComparison.Ordinal)
            .Replace("{theme}", club.Theme, StringComparison.Ordinal)
            .Replace("{tone}", club.Tone, StringComparison.Ordinal)
            .Replace("{city}", club.City, StringComparison.Ordinal)
            .Replace("{location}", club.Location, StringComparison.Ordinal);
    }
}
