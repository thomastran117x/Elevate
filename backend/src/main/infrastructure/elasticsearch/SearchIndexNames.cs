namespace backend.main.infrastructure.elasticsearch;

public sealed class SearchIndexNames
{
    public const string EventsConfigurationKey = "Elasticsearch:Indices:Events";
    public const string ClubsConfigurationKey = "Elasticsearch:Indices:Clubs";
    public const string ClubPostsConfigurationKey = "Elasticsearch:Indices:ClubPosts";

    public string Events { get; init; } = "events";

    public string Clubs { get; init; } = "clubs";

    public string ClubPosts { get; init; } = "club_posts";

    public static SearchIndexNames FromConfiguration(IConfiguration configuration) => new()
    {
        Events = GetName(configuration, EventsConfigurationKey, "events"),
        Clubs = GetName(configuration, ClubsConfigurationKey, "clubs"),
        ClubPosts = GetName(configuration, ClubPostsConfigurationKey, "club_posts")
    };

    private static string GetName(IConfiguration configuration, string key, string fallback) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!.Trim();
}
